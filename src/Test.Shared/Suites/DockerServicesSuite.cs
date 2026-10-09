namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Integration tests that run the database and dependency-audit skills against real services and real tools in
    /// throwaway Docker containers: PostgreSQL, MySQL, MariaDB, SQL Server, Oracle, MongoDB, Redis, Neo4j, Cassandra,
    /// and LiteGraph, plus npm audit, pip-audit, and osv-scanner output. Each case starts its own container, reaches it
    /// through shims that forward the skill's client calls into the container (so no client is installed on the
    /// host), checks the results and the read-only guarantees against the real server, checks that no secret reaches
    /// the output, and removes the container. Opt-in: <c>--docker</c> (or <c>MUX_TEST_DOCKER=1</c>) on Linux or macOS.
    /// </summary>
    public static class DockerServicesSuite
    {
        #region Private-Members

        private const string SuiteId = "DockerServices";

        private static readonly TimeSpan _Startup = TimeSpan.FromMinutes(4);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool enabled = DockerHarness.IsEnabled && IsOnPath("pwsh");
            string reason = DockerHarness.Requested && !IsOnPath("pwsh") ? "pwsh is not on PATH" : DockerHarness.SkipReason;
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, CancellationToken, Task> body, bool skip = false, string extraReason = "")
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync((SkillTestContext c) => body(c, ct), ct),
                    skip: !enabled || skip, skipReason: !enabled ? reason : extraReason));
            }

            Add("Postgres", "sql-postgres against PostgreSQL 16: version, tables, columns, rows, and the server rejecting a write hidden in a SELECT", PostgresAsync);
            Add("MySql", "sql-mysql against MySQL 8.4: version, tables, columns, rows, read-only refusals, and nothing written", (c, ct) => MySqlFamilyAsync(c, ct, "mysql:8.4", "mysql", "mysqladmin"));
            Add("MariaDb", "sql-mysql against MariaDB 11.4 through its mariadb client", (c, ct) => MySqlFamilyAsync(c, ct, "mariadb:11.4", "mariadb", "mariadb-admin"));
            Add("SqlServer", "sql-sqlserver against SQL Server 2022: version, tables, columns, rows, and a SELECT INTO rolled back",
                SqlServerAsync, DockerHarness.IsArm64Host && Environment.GetEnvironmentVariable("MUX_TEST_DOCKER_EMULATION") != "1",
                "the SQL Server image is x64 only; set MUX_TEST_DOCKER_EMULATION=1 to try it under emulation on ARM64");
            Add("Oracle", "sql-oracle against Oracle Database 23ai Free: version, tables, columns, rows, and SELECT FOR UPDATE refused by the read-only transaction", OracleAsync);
            Add("MongoDb", "nosql-mongodb against MongoDB 8: ping, databases, collections, filtered find, count, and indexes", MongoAsync);
            Add("Redis", "nosql-redis against Redis 7.4 with a password: info, SCAN, and type-aware reads of a string, hash, and list", RedisAsync);
            Add("Neo4j", "graph-neo4j against Neo4j 5: version, labels, relationship types, a read query, and a write rejected in read access mode", Neo4jAsync);
            Add("Cassandra", "nosql-cassandra against Cassandra 5: keyspaces, tables, schema, and a SELECT", CassandraAsync);
            Add("LiteGraph", "graph-litegraph against a LiteGraph server: health, tenants, graphs, nodes, a native query, and a token that never prints", LiteGraphAsync);
            Add("NpmAudit", "deps-audit reads real npm audit output for a lockfile with a known-vulnerable lodash", NpmAuditAsync);
            Add("PipAudit", "deps-audit reads real pip-audit output for a known-vulnerable requests", PipAuditAsync);
            Add("OsvScanner", "deps-audit reads real osv-scanner output for a known-vulnerable lockfile", OsvScannerAsync);

            return new TestSuiteDescriptor(SuiteId, "Database and audit skills against real services in Docker", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task PostgresAsync(SkillTestContext c, CancellationToken ct)
        {
            const string password = "S3cretPg9";
            using (DockerContainer db = DockerHarness.Start("postgres:16-alpine", new[] { "-e", "POSTGRES_PASSWORD=" + password }))
            {
                await db.WaitUntilReadyAsync(_Startup, ct, "psql", "-U", "postgres", "-c", "select 1").ConfigureAwait(false);
                Check(db.Exec("psql", "-U", "postgres", "-v", "ON_ERROR_STOP=1", "-c", "create table orders(id serial primary key, status text not null); insert into orders(status) values ('open'), ('done'); create sequence audit_seq;"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["psql"] = "psql" }, new[] { "PGOPTIONS" });
                env["DATABASE_URL"] = "postgres://postgres:" + password + "@localhost:5432/postgres";
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "sql-postgres", "ping").ConfigureAwait(false)).Exit(0).Has("PostgreSQL 16"),
                    (await c.Run(false, env, "sql-postgres", "tables").ConfigureAwait(false)).Exit(0).Has("orders"),
                    (await c.Run(false, env, "sql-postgres", "describe", "public.orders").ConfigureAwait(false)).Exit(0).Has("status").Has("text"),
                    (await c.Run(false, env, "sql-postgres", "query", "select status from orders order by id").ConfigureAwait(false)).Exit(0).Has("open").Has("done"),
                    (await c.Run(false, env, "sql-postgres", "query", "select nextval('audit_seq')").ConfigureAwait(false)).Exit(1).Has("read-only transaction"),
                    (await c.Run(false, env, "sql-postgres", "query", "delete from orders").ConfigureAwait(false)).Exit(2)
                };
                NoSecret(runs, password);
                MuxAssert.Contains("2", db.Exec("psql", "-U", "postgres", "-tA", "-c", "select count(*) from orders").StandardOutput, "nothing was written");
            }
        }

        private static async Task MySqlFamilyAsync(SkillTestContext c, CancellationToken ct, string image, string client, string admin)
        {
            const string password = "S3cretMy9";
            string root = image.StartsWith("mariadb", StringComparison.Ordinal) ? "MARIADB_ROOT_PASSWORD" : "MYSQL_ROOT_PASSWORD";
            string database = image.StartsWith("mariadb", StringComparison.Ordinal) ? "MARIADB_DATABASE" : "MYSQL_DATABASE";
            using (DockerContainer db = DockerHarness.Start(image, new[] { "-e", root + "=" + password, "-e", database + "=app" }))
            {
                await db.WaitUntilReadyAsync(_Startup, ct, client, "-h127.0.0.1", "-uroot", "-p" + password, "app", "-e", "select 1").ConfigureAwait(false);
                Check(db.Exec(client, "-h127.0.0.1", "-uroot", "-p" + password, "app", "-e", "create table orders(id int primary key auto_increment, status varchar(20) not null); insert into orders(status) values ('open'), ('done');"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["mysql"] = client }, new[] { "MYSQL_PWD" });
                env["DATABASE_URL"] = "mysql://root:" + password + "@127.0.0.1:3306/app";
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "sql-mysql", "ping").ConfigureAwait(false)).Exit(0).Has("version"),
                    (await c.Run(false, env, "sql-mysql", "tables").ConfigureAwait(false)).Exit(0).Has("orders"),
                    (await c.Run(false, env, "sql-mysql", "describe", "orders").ConfigureAwait(false)).Exit(0).Has("status").Has("varchar"),
                    (await c.Run(false, env, "sql-mysql", "query", "select status from orders order by id").ConfigureAwait(false)).Exit(0).Has("open").Has("done"),
                    (await c.Run(false, env, "sql-mysql", "query", "select * from orders into outfile '/tmp/orders.csv'").ConfigureAwait(false)).Exit(2).Has("writes a file on the database server"),
                    (await c.Run(false, env, "sql-mysql", "query", "update orders set status = 'x'").ConfigureAwait(false)).Exit(2)
                };
                NoSecret(runs, password);
                MuxAssert.Contains("2", db.Exec(client, "-h127.0.0.1", "-uroot", "-p" + password, "app", "-N", "-e", "select count(*) from orders where status in ('open','done')").StandardOutput, "nothing was written");
                MuxAssert.AreNotEqual(0, db.Exec("test", "-e", "/tmp/orders.csv").ExitCode, "no file was written on the server");
                _ = admin;
            }
        }

        private static async Task SqlServerAsync(SkillTestContext c, CancellationToken ct)
        {
            const string password = "S3cret!Ms9xQ";
            const string sqlcmd = "/opt/mssql-tools18/bin/sqlcmd";
            using (DockerContainer db = DockerHarness.Start("mcr.microsoft.com/mssql/server:2022-latest", new[] { "-e", "ACCEPT_EULA=Y", "-e", "MSSQL_SA_PASSWORD=" + password }))
            {
                await db.WaitUntilReadyAsync(_Startup, ct, sqlcmd, "-C", "-S", "localhost", "-U", "sa", "-P", password, "-Q", "select 1").ConfigureAwait(false);
                Check(db.Exec(sqlcmd, "-C", "-S", "localhost", "-U", "sa", "-P", password, "-b", "-Q", "create database app; exec('use app; create table orders(id int identity primary key, status varchar(20) not null); insert into orders(status) values (''open''), (''done'');')"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["sqlcmd"] = sqlcmd }, new[] { "SQLCMDPASSWORD" });
                env["SQLSERVER_CONNECTION_STRING"] = "Server=localhost,1433;Database=app;User Id=sa;Password=" + password + ";TrustServerCertificate=True";
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "sql-sqlserver", "ping").ConfigureAwait(false)).Exit(0).Has("Microsoft SQL Server 2022"),
                    (await c.Run(false, env, "sql-sqlserver", "tables").ConfigureAwait(false)).Exit(0).Has("orders"),
                    (await c.Run(false, env, "sql-sqlserver", "describe", "dbo.orders").ConfigureAwait(false)).Exit(0).Has("status").Has("varchar"),
                    (await c.Run(false, env, "sql-sqlserver", "query", "select status from orders order by id").ConfigureAwait(false)).Exit(0).Has("open").Has("done"),
                    (await c.Run(false, env, "sql-sqlserver", "query", "select * into orders_copy from orders").ConfigureAwait(false)).Exit(0)
                };
                NoSecret(runs, password);
                MuxAssert.Contains("0", db.Exec(sqlcmd, "-C", "-S", "localhost", "-U", "sa", "-P", password, "-d", "app", "-h", "-1", "-Q", "set nocount on; select count(*) from sys.tables where name = 'orders_copy'").StandardOutput, "the SELECT INTO was rolled back");
            }
        }

        private static async Task OracleAsync(SkillTestContext c, CancellationToken ct)
        {
            const string password = "S3cretOra9";
            using (DockerContainer db = DockerHarness.Start("gvenzl/oracle-free:23-slim-faststart", new[] { "-e", "ORACLE_PASSWORD=" + password }))
            {
                await db.WaitUntilReadyAsync(TimeSpan.FromMinutes(6), ct, "healthcheck.sh").ConfigureAwait(false);
                Check(db.Exec("bash", "-c", "printf \"create table orders(id number primary key, status varchar2(20) not null);\\ninsert into orders values (1, 'open');\\ninsert into orders values (2, 'done');\\ncommit;\\nexit\\n\" | sqlplus -S -L system/" + password + "@//localhost:1521/FREEPDB1"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["sqlplus"] = "sqlplus" }, Array.Empty<string>());
                env["ORACLE_CONNECT"] = "system/" + password + "@//localhost:1521/FREEPDB1";
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "sql-oracle", "ping").ConfigureAwait(false)).Exit(0).Has("Oracle"),
                    (await c.Run(false, env, "sql-oracle", "tables").ConfigureAwait(false)).Exit(0).Has("ORDERS"),
                    (await c.Run(false, env, "sql-oracle", "describe", "orders").ConfigureAwait(false)).Exit(0).Has("STATUS").Has("VARCHAR2"),
                    (await c.Run(false, env, "sql-oracle", "query", "select status from orders order by id").ConfigureAwait(false)).Exit(0).Has("open").Has("done"),
                    (await c.Run(false, env, "sql-oracle", "query", "select * from orders for update").ConfigureAwait(false)).Exit(1).Has("ORA-")
                };
                NoSecret(runs, password);
            }
        }

        private static async Task MongoAsync(SkillTestContext c, CancellationToken ct)
        {
            const string password = "S3cretMg9";
            using (DockerContainer db = DockerHarness.Start("mongo:8", new[] { "-e", "MONGO_INITDB_ROOT_USERNAME=root", "-e", "MONGO_INITDB_ROOT_PASSWORD=" + password }))
            {
                await db.WaitUntilReadyAsync(_Startup, ct, "mongosh", "--quiet", "-u", "root", "-p", password, "--eval", "db.runCommand({ ping: 1 }).ok").ConfigureAwait(false);
                Check(db.Exec("mongosh", "--quiet", "-u", "root", "-p", password, "--eval", "db.getSiblingDB('app').orders.insertMany([{ n: 1, status: 'open' }, { n: 2, status: 'done' }])"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["mongosh"] = "mongosh" }, new[] { "MONGODB_URI" });
                env["MONGODB_URI"] = "mongodb://root:" + password + "@localhost:27017/app?authSource=admin";
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "nosql-mongodb", "ping").ConfigureAwait(false)).Exit(0).Has("server version: 8").Has("database: app"),
                    (await c.Run(false, env, "nosql-mongodb", "databases").ConfigureAwait(false)).Exit(0).Has("app"),
                    (await c.Run(false, env, "nosql-mongodb", "collections").ConfigureAwait(false)).Exit(0).Has("orders"),
                    (await c.Run(false, env, "nosql-mongodb", "find", "orders", "{\"status\":\"open\"}").ConfigureAwait(false)).Exit(0).Has("open").Lacks("done"),
                    (await c.Run(false, env, "nosql-mongodb", "count", "orders").ConfigureAwait(false)).Exit(0).Has("2"),
                    (await c.Run(false, env, "nosql-mongodb", "indexes", "orders").ConfigureAwait(false)).Exit(0).Has("_id_")
                };
                NoSecret(runs, password);
            }
        }

        private static async Task RedisAsync(SkillTestContext c, CancellationToken ct)
        {
            const string password = "S3cretRd9";
            using (DockerContainer db = DockerHarness.Start("redis:7.4", null, new[] { "redis-server", "--requirepass", password }))
            {
                await db.WaitUntilReadyAsync(_Startup, ct, "redis-cli", "-a", password, "--no-auth-warning", "ping").ConfigureAwait(false);
                Check(db.Exec("redis-cli", "-a", password, "--no-auth-warning", "set", "user:1", "ada"), "seed string");
                Check(db.Exec("redis-cli", "-a", password, "--no-auth-warning", "hset", "user:2", "name", "linus", "lang", "c"), "seed hash");
                Check(db.Exec("redis-cli", "-a", password, "--no-auth-warning", "rpush", "queue", "a", "b", "c"), "seed list");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["redis-cli"] = "redis-cli" }, new[] { "REDISCLI_AUTH" });
                env["REDIS_URL"] = "redis://:" + password + "@localhost:6379/0";
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "nosql-redis", "ping").ConfigureAwait(false)).Exit(0).Has("redis_version:7.4"),
                    (await c.Run(false, env, "nosql-redis", "keys", "user:*").ConfigureAwait(false)).Exit(0).Has("user:1").Has("user:2").Lacks("queue"),
                    (await c.Run(false, env, "nosql-redis", "get", "user:1").ConfigureAwait(false)).Exit(0).Has("type: string").Has("ada"),
                    (await c.Run(false, env, "nosql-redis", "get", "user:2").ConfigureAwait(false)).Exit(0).Has("type: hash").Has("linus"),
                    (await c.Run(false, env, "nosql-redis", "get", "queue").ConfigureAwait(false)).Exit(0).Has("type: list").Has("b"),
                    (await c.Run(false, env, "nosql-redis", "get", "missing").ConfigureAwait(false)).Exit(1).Has("No key named missing")
                };
                NoSecret(runs, password);
            }
        }

        private static async Task Neo4jAsync(SkillTestContext c, CancellationToken ct)
        {
            const string password = "S3cretNeo9";
            using (DockerContainer db = DockerHarness.Start("neo4j:5-community", new[] { "-e", "NEO4J_AUTH=neo4j/" + password }))
            {
                await db.WaitUntilReadyAsync(_Startup, ct, "cypher-shell", "-u", "neo4j", "-p", password, "RETURN 1").ConfigureAwait(false);
                Check(db.Exec("cypher-shell", "-u", "neo4j", "-p", password, "CREATE (:Person {name: 'Ada'})-[:KNOWS]->(:Person {name: 'Linus'})"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["cypher-shell"] = "cypher-shell" }, new[] { "NEO4J_URI", "NEO4J_USERNAME", "NEO4J_PASSWORD" });
                env["NEO4J_URI"] = "bolt://localhost:7687";
                env["NEO4J_USERNAME"] = "neo4j";
                env["NEO4J_PASSWORD"] = password;
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "graph-neo4j", "ping").ConfigureAwait(false)).Exit(0).Has("Neo4j Kernel"),
                    (await c.Run(false, env, "graph-neo4j", "labels").ConfigureAwait(false)).Exit(0).Has("Person"),
                    (await c.Run(false, env, "graph-neo4j", "relationships").ConfigureAwait(false)).Exit(0).Has("KNOWS"),
                    (await c.Run(false, env, "graph-neo4j", "query", "MATCH (p:Person) RETURN count(p) AS people").ConfigureAwait(false)).Exit(0).Has("2"),
                    (await c.Run(false, env, "graph-neo4j", "query", "CREATE (:Person {name: 'Grace'})").ConfigureAwait(false)).Exit(1)
                };
                NoSecret(runs, password);
                MuxAssert.Contains("2", db.Exec("cypher-shell", "-u", "neo4j", "-p", password, "--format", "plain", "MATCH (p:Person) RETURN count(p)").StandardOutput, "the write was rejected");
            }
        }

        private static async Task CassandraAsync(SkillTestContext c, CancellationToken ct)
        {
            using (DockerContainer db = DockerHarness.Start("cassandra:5", new[] { "-e", "MAX_HEAP_SIZE=512M", "-e", "HEAP_NEWSIZE=128M" }))
            {
                await db.WaitUntilReadyAsync(TimeSpan.FromMinutes(6), ct, "cqlsh", "-e", "DESCRIBE KEYSPACES").ConfigureAwait(false);
                Check(db.Exec("cqlsh", "-e", "CREATE KEYSPACE shop WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1}; CREATE TABLE shop.orders (id int PRIMARY KEY, status text); INSERT INTO shop.orders (id, status) VALUES (1, 'open');"), "seed");
                Dictionary<string, string> env = Env(c, db, new Dictionary<string, string> { ["cqlsh"] = "cqlsh" }, Array.Empty<string>());
                (await c.Run(false, env, "nosql-cassandra", "keyspaces").ConfigureAwait(false)).Exit(0).Has("shop");
                (await c.Run(false, env, "nosql-cassandra", "tables", "shop").ConfigureAwait(false)).Exit(0).Has("orders");
                (await c.Run(false, env, "nosql-cassandra", "describe", "shop.orders").ConfigureAwait(false)).Exit(0).Has("CREATE TABLE shop.orders");
                (await c.Run(false, env, "nosql-cassandra", "query", "SELECT status FROM shop.orders WHERE id = 1").ConfigureAwait(false)).Exit(0).Has("open");
            }
        }

        private static async Task LiteGraphAsync(SkillTestContext c, CancellationToken ct)
        {
            const string token = "S3cretLg9token";
            const string zero = "00000000-0000-0000-0000-000000000000";
            const string image = "jchristn77/litegraph:v10.2.0";

            // LiteGraph listens on localhost unless its settings file says otherwise, and has no variable for that, so
            // let the image write its own default settings in an init-only run, then listen on every interface.
            string settingsDir = Path.Combine(c.Root, "litegraph-settings");
            Directory.CreateDirectory(settingsDir);
            DockerResult init = DockerHarness.Run(TimeSpan.FromMinutes(5), "run", "--rm", "--label", DockerHarness.Label, "-v", settingsDir + ":/out", "-e", "LITEGRAPH_INIT_ONLY=true",
                "--entrypoint", "sh", image, "-c", "dotnet LiteGraph.Server.dll >/dev/null 2>&1; cp /app/litegraph.json /out/litegraph.json; chmod -R a+rwX /out");
            string settingsPath = Path.Combine(settingsDir, "litegraph.json");
            MuxAssert.IsTrue(File.Exists(settingsPath), "LiteGraph wrote its default settings: " + init.StandardError);
            System.Text.Json.Nodes.JsonNode settings = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settingsPath))!;
            settings["Rest"]!["Hostname"] = "*";

            // On Linux the init container wrote the file as root, so write the edited copy somewhere this process owns.
            string mountedSettings = Path.Combine(c.Root, "litegraph-server.json");
            File.WriteAllText(mountedSettings, settings.ToJsonString());
            settingsPath = mountedSettings;

            using (DockerContainer server = DockerHarness.Start(image, new[] { "-p", "127.0.0.1::8701", "-v", settingsPath + ":/app/litegraph.json", "-e", "LITEGRAPH_ADMIN_BEARER_TOKEN=" + token, "-e", "LITEGRAPH_CREATE_DEFAULT_RECORDS=true" }))
            {
                string endpoint = "http://127.0.0.1:" + server.HostPort(8701);
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    DateTime deadline = DateTime.UtcNow + _Startup;
                    while (true)
                    {
                        try
                        {
                            if ((await http.GetAsync(endpoint + "/v1.0/health/ready", ct).ConfigureAwait(false)).IsSuccessStatusCode) break;
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }

                        if (DateTime.UtcNow > deadline) throw new TimeoutException("LiteGraph was not ready: " + server.Logs());
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                    }

                    using (HttpRequestMessage create = new HttpRequestMessage(HttpMethod.Put, endpoint + "/v1.0/tenants/" + zero + "/graphs/" + zero + "/nodes"))
                    {
                        create.Headers.Add("Authorization", "Bearer " + token);
                        create.Content = new StringContent("{\"Name\":\"Ada Lovelace\",\"Labels\":[\"Person\"]}", Encoding.UTF8, "application/json");
                        HttpResponseMessage created = await http.SendAsync(create, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(created.IsSuccessStatusCode, "seed node: " + (int)created.StatusCode + " " + await created.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    }
                }

                Dictionary<string, string> env = new Dictionary<string, string> { ["LITEGRAPH_ENDPOINT"] = endpoint, ["LITEGRAPH_API_KEY"] = token, ["LITEGRAPH_TENANT_GUID"] = zero, ["LITEGRAPH_GRAPH_GUID"] = zero };
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(false, env, "graph-litegraph", "ping").ConfigureAwait(false)).Exit(0),
                    (await c.Run(false, env, "graph-litegraph", "tenants").ConfigureAwait(false)).Exit(0).Has("Default tenant"),
                    (await c.Run(false, env, "graph-litegraph", "graphs").ConfigureAwait(false)).Exit(0).Has("Default graph"),
                    (await c.Run(false, env, "graph-litegraph", "stats").ConfigureAwait(false)).Exit(0),
                    (await c.Run(false, env, "graph-litegraph", "nodes", "--max", "10").ConfigureAwait(false)).Exit(0).Has("Ada Lovelace"),
                    (await c.Run(false, env, "graph-litegraph", "query", "MATCH (n) RETURN n LIMIT 5").ConfigureAwait(false)).Exit(0).Has("Ada Lovelace"),
                    (await c.Run(false, env, "graph-litegraph", "query", "MATCH (n) SET n.name = 'x' RETURN n").ConfigureAwait(false)).Exit(2)
                };
                NoSecret(runs, token);
                env["LITEGRAPH_API_KEY"] = "wrong-token";
                (await c.Run(false, env, "graph-litegraph", "tenants").ConfigureAwait(false)).Exit(1).Has("HTTP 401");
            }
        }

        private static async Task NpmAuditAsync(SkillTestContext c, CancellationToken ct)
        {
            string work = Fixture(c, "npm", new Dictionary<string, string> { ["package.json"] = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"dependencies\":{\"lodash\":\"4.17.15\"}}" });
            DockerResult run = DockerHarness.Run(TimeSpan.FromMinutes(8), "run", "--rm", "--label", DockerHarness.Label, "-v", work + ":/w", "-w", "/w", "node:22-alpine", "sh", "-c",
                "npm install --package-lock-only --ignore-scripts --no-audit --no-fund >/dev/null && npm audit --json > /w/npm-audit.json; test -s /w/npm-audit.json");
            MuxAssert.AreEqual(0, run.ExitCode, "npm audit ran: " + run.StandardError);
            (await c.Run("deps-audit", "audit", "--from", "npm=" + Path.Combine(work, "npm-audit.json")).ConfigureAwait(false))
                .Exit(1).Has("lodash").Has("GHSA-").Has("at or above high");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task PipAuditAsync(SkillTestContext c, CancellationToken ct)
        {
            string work = Fixture(c, "pip", new Dictionary<string, string> { ["requirements.txt"] = "requests==2.25.0\n" });
            DockerResult run = DockerHarness.Run(TimeSpan.FromMinutes(8), "run", "--rm", "--label", DockerHarness.Label, "-v", work + ":/w", "-w", "/w", "python:3.12-alpine", "sh", "-c",
                "pip install -q --disable-pip-version-check pip-audit >/dev/null 2>&1; pip-audit -r requirements.txt -f json -o /w/pip-audit.json; test -s /w/pip-audit.json");
            MuxAssert.AreEqual(0, run.ExitCode, "pip-audit ran: " + run.StandardError + run.StandardOutput);
            (await c.Run("deps-audit", "audit", "--from", "pip-audit=" + Path.Combine(work, "pip-audit.json")).ConfigureAwait(false))
                .Exit(1).Has("python").Has("requests").Has("2.25.0");
        }

        private static async Task OsvScannerAsync(SkillTestContext c, CancellationToken ct)
        {
            string work = Fixture(c, "osv", new Dictionary<string, string> { ["requirements.txt"] = "requests==2.25.0\n" });
            DockerResult run = DockerHarness.Run(TimeSpan.FromMinutes(8), "run", "--rm", "--label", DockerHarness.Label, "-v", work + ":/src", "ghcr.io/google/osv-scanner:latest", "scan", "--format", "json", "-r", "/src");
            MuxAssert.IsTrue(run.StandardOutput.Contains("\"results\"", StringComparison.Ordinal), "osv-scanner produced JSON: " + run.StandardError);
            File.WriteAllText(Path.Combine(work, "osv.json"), run.StandardOutput);
            (await c.Run("deps-audit", "audit", "--from", "osv-scanner=" + Path.Combine(work, "osv.json")).ConfigureAwait(false))
                .Has("requests").Has("2.25.0");
        }

        private static Dictionary<string, string> Env(SkillTestContext c, DockerContainer container, IReadOnlyDictionary<string, string> tools, IEnumerable<string> passThrough)
        {
            string shims = DockerHarness.WriteShims(Path.Combine(c.Root, "shims-" + container.Name), container, tools, passThrough);
            return new Dictionary<string, string> { ["PATH"] = DockerHarness.PathWith(shims) };
        }

        private static string Fixture(SkillTestContext c, string name, Dictionary<string, string> files)
        {
            string dir = Path.Combine(c.Root, "fixture-" + name);
            Directory.CreateDirectory(dir);
            foreach (KeyValuePair<string, string> file in files) File.WriteAllText(Path.Combine(dir, file.Key), file.Value);
            return dir;
        }

        private static void Check(DockerResult result, string step)
        {
            MuxAssert.AreEqual(0, result.ExitCode, step + ": " + (result.StandardOutput + result.StandardError).Trim());
        }

        private static void NoSecret(IEnumerable<SkillRunResult> runs, string secret)
        {
            foreach (SkillRunResult run in runs)
            {
                MuxAssert.DoesNotContain(secret, run.Stdout + run.Stderr, "no secret in the output");
            }
        }

        private static bool IsOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string candidate in new[] { executable, executable + ".exe" })
                {
                    if (File.Exists(Path.Combine(directory, candidate))) return true;
                }
            }

            return false;
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-docker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                Directory.CreateDirectory(Path.Combine(root, "project"));
                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
