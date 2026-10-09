namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the database skills: db-migrate detection, plans, and the production guard; the read-only
    /// SQL, NoSQL, and graph platform skills (real SQLite, dry runs elsewhere, LiteGraph against a stub server); and that
    /// no connection secret ever reaches the output. Positive and negative cases.
    /// </summary>
    public static class DatabaseSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "DatabaseSkills";

        private const string Secret = "S3CRETvalue";

        private static readonly string[] _Platforms =
        {
            "sql-sqlite", "sql-postgres", "sql-mysql", "sql-sqlserver", "sql-oracle",
            "nosql-mongodb", "nosql-redis", "nosql-dynamodb", "nosql-cassandra", "graph-neo4j", "graph-litegraph"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            bool sqlite = ready && IsOnPath("sqlite3");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body, bool skip = false, string reason = "")
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready || skip, skipReason: !ready ? "pwsh is not on PATH" : reason));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "Twelve database skills in the data category: db-migrate mutating, every platform skill read-only and gated on its client", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                MuxAssert.IsTrue(defs["db-migrate"].Mutating, "db-migrate applies migrations");
                MuxAssert.IsFalse(defs.ContainsKey("db-query"), "db-query was replaced by the platform skills");
                foreach (string id in _Platforms.Concat(new[] { "db-migrate" }))
                {
                    MuxAssert.IsTrue(defs.ContainsKey(id), id + " exists");
                    MuxAssert.AreEqual("data", DefaultSkillCategories.For(id), id + " category");
                    if (id != "db-migrate") MuxAssert.IsFalse(defs[id].Mutating, id + " is read-only");
                }

                MuxAssert.AreEqual("psql", string.Join(",", defs["sql-postgres"].RequiresTools), "postgres needs psql");
                MuxAssert.AreEqual("mysql|mariadb", string.Join(",", defs["sql-mysql"].RequiresTools), "either MySQL client");
                MuxAssert.AreEqual(0, defs["graph-litegraph"].RequiresTools.Count, "LiteGraph needs only pwsh");
                MuxAssert.AreEqual("ping,tables,describe,query", string.Join(",", defs["sql-oracle"].Commands.Select(c => c.Name)), "SQL commands");
                return Task.CompletedTask;
            }));

            Add("SqliteReadsAndRefusesWrites", "sql-sqlite reads a real database, refuses write statements and stacked statements, and -readonly blocks what gets past", async (SkillTestContext c) =>
            {
                string db = Path.Combine(c.Project, "app.db");
                Sqlite(db, "create table users(id integer primary key, name text); insert into users(name) values ('ada'), ('linus');");
                (await c.Run("sql-sqlite", "ping", db).ConfigureAwait(false)).Exit(0).Has("sqlite_version");
                (await c.Run("sql-sqlite", "tables", db).ConfigureAwait(false)).Exit(0).Has("users");
                (await c.Run("sql-sqlite", "describe", db, "users").ConfigureAwait(false)).Exit(0).Has("INTEGER").Has("TEXT");
                (await c.Run("sql-sqlite", "query", db, "select name from users where name = 'a;b' or id = 2").ConfigureAwait(false)).Exit(0).Has("linus").Lacks("ada");
                (await c.Run("sql-sqlite", "query", db, "-- leading comment\nselect count(*) from users").ConfigureAwait(false)).Exit(0).Has("2");
                (await c.Run("sql-sqlite", "query", db, "delete from users").ConfigureAwait(false)).Exit(2).Has("DELETE is not one");
                (await c.Run("sql-sqlite", "query", db, "/* hide */ drop table users").ConfigureAwait(false)).Exit(2).Has("DROP is not one");
                (await c.Run("sql-sqlite", "query", db, "select 1; drop table users").ConfigureAwait(false)).Exit(2).Has("one statement at a time");
                (await c.Run("sql-sqlite", "query", db, "pragma user_version = 7").ConfigureAwait(false)).Exit(1).Has("readonly");
                (await c.Run("sql-sqlite", "describe", db, "users; drop").ConfigureAwait(false)).Exit(2).Has("plain table name");
                (await c.Run("sql-sqlite", "tables", Path.Combine(c.Project, "missing.db")).ConfigureAwait(false)).Exit(2).Has("database file not found");
                (await c.Run("sql-sqlite", "tables").ConfigureAwait(false)).Exit(2).Has("pass the database file");
                (await c.Run("sql-sqlite", "query", db, "select * from users", "--limit", "1").ConfigureAwait(false)).Exit(0).Has("output cut at 1 lines");
                MuxAssert.AreEqual("2", Sqlite(db, "select count(*) from users").Trim(), "nothing was written");
            }, !sqlite, "sqlite3 is not installed");

            Add("SqlServerPlatformsDryRun", "Postgres, MySQL, SQL Server, and Oracle build read-only command lines from the connection variable and never print the secret", async (SkillTestContext c) =>
            {
                Dictionary<string, string> env = new Dictionary<string, string>
                {
                    ["DATABASE_URL"] = "postgres://app:" + Secret + "@db.local:5432/shop",
                    ["MY_URL"] = "mysql://app:" + Secret + "@my.local:3307/shop",
                    ["SQLSERVER_CONNECTION_STRING"] = "Server=tcp:sql.local,1433;Database=shop;User Id=sa;Password=" + Secret + ";TrustServerCertificate=True",
                    ["ORACLE_CONNECT"] = "scott/" + Secret + "@//ora.local:1521/XEPDB1"
                };
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(true, env, "sql-postgres", "describe", "public.orders").ConfigureAwait(false)).Exit(0).Has("--dbname $DATABASE_URL").Has("table_schema = 'public' and table_name = 'orders'"),
                    (await c.Run(true, env, "sql-postgres", "query", "select * from orders limit 5").ConfigureAwait(false)).Exit(0).Has("select * from orders limit 5"),
                    (await c.Run(true, env, "sql-mysql", "query", "show tables", "--url-env", "MY_URL").ConfigureAwait(false)).Exit(0).Has("--host my.local --port 3307 --user app --database shop").Has("START TRANSACTION READ ONLY; show tables; ROLLBACK;"),
                    (await c.Run(true, env, "sql-sqlserver", "tables").ConfigureAwait(false)).Exit(0).Has("-S sql.local,1433 -d shop -U sa -C").Has("BEGIN TRANSACTION;").Has("ROLLBACK TRANSACTION;"),
                    (await c.Run(true, env, "sql-oracle", "describe", "hr.employees").ConfigureAwait(false)).Exit(0).Has("CONNECT $ORACLE_CONNECT").Has("SET TRANSACTION READ ONLY;").Has("owner = 'HR' and table_name = 'EMPLOYEES'")
                };
                foreach (SkillRunResult run in runs)
                {
                    MuxAssert.DoesNotContain(Secret, run.Stdout + run.Stderr, "no secret in the output");
                }
            });

            Add("SqlRefusesWritesEverywhere", "Each SQL platform refuses write statements and connection strings passed as --url-env", async (SkillTestContext c) =>
            {
                Dictionary<string, string> env = new Dictionary<string, string> { ["DATABASE_URL"] = "postgres://x@h/db", ["SQLSERVER_CONNECTION_STRING"] = "Server=h", ["ORACLE_CONNECT"] = "a/b@c" };
                (await c.Run(true, env, "sql-postgres", "query", "insert into t values (1)").ConfigureAwait(false)).Exit(2).Has("INSERT is not one");
                (await c.Run(true, env, "sql-mysql", "query", "update t set x = 1").ConfigureAwait(false)).Exit(2).Has("UPDATE is not one");
                (await c.Run(true, env, "sql-sqlserver", "query", "exec sp_who").ConfigureAwait(false)).Exit(2).Has("EXEC is not one");
                (await c.Run(true, env, "sql-oracle", "query", "truncate table t").ConfigureAwait(false)).Exit(2).Has("TRUNCATE is not one");
                (await c.Run(true, env, "sql-postgres", "query", "with x as (select 1) select * from x").ConfigureAwait(false)).Exit(0);
                (await c.Run(true, env, "sql-postgres", "tables", "--url-env", "postgres://a:b@h/db").ConfigureAwait(false)).Exit(2).Has("never the connection string");
                (await c.Run(true, env, "sql-postgres", "query").ConfigureAwait(false)).Exit(2).Has("usage: sql-postgres query");
            });

            Add("MissingConnectionIsExplained", "A real run without the connection variable exits 2 naming the variable", async (SkillTestContext c) =>
            {
                Dictionary<string, string> env = new Dictionary<string, string> { ["DATABASE_URL"] = string.Empty, ["MONGODB_URI"] = string.Empty };
                (await c.Run(false, env, "sql-postgres", "tables").ConfigureAwait(false)).Exit(2).Has("$DATABASE_URL is not set");
                (await c.Run(false, env, "nosql-mongodb", "collections").ConfigureAwait(false)).Exit(2).Has("$MONGODB_URI is not set");
            });

            Add("NoSqlDryRuns", "MongoDB, Redis, DynamoDB, and Cassandra offer only reads, validate their input, and keep credentials off the command line", async (SkillTestContext c) =>
            {
                Dictionary<string, string> env = new Dictionary<string, string>
                {
                    ["MONGODB_URI"] = "mongodb://u:" + Secret + "@m.local/shop",
                    ["REDIS_URL"] = "rediss://cache:" + Secret + "@r.local:6380/2",
                    ["CASSANDRA_USERNAME"] = "cass",
                    ["CASSANDRA_PASSWORD"] = Secret
                };
                List<SkillRunResult> runs = new List<SkillRunResult>
                {
                    (await c.Run(true, env, "nosql-mongodb", "find", "orders", "{\"status\":\"open\"}", "--docs", "5").ConfigureAwait(false)).Exit(0).Has("connect(process.env.MONGODB_URI)").Has("getCollection(").Has(".limit(5)"),
                    (await c.Run(true, env, "nosql-mongodb", "count", "orders").ConfigureAwait(false)).Exit(0).Has("countDocuments"),
                    (await c.Run(true, env, "nosql-redis", "keys", "user:*").ConfigureAwait(false)).Exit(0).Has("-h r.local -p 6380").Has("-n 2").Has("--tls").Has("--user cache").Has("--scan").Has("user:*"),
                    (await c.Run(true, env, "nosql-redis", "get", "session:1").ConfigureAwait(false)).Exit(0).Has("GET session:1"),
                    (await c.Run(true, env, "nosql-dynamodb", "scan", "Orders", "--items", "5", "--region", "us-west-2").ConfigureAwait(false)).Exit(0).Has("aws dynamodb scan --table-name Orders --max-items 5").Has("--region us-west-2"),
                    (await c.Run(true, env, "nosql-cassandra", "tables", "shop").ConfigureAwait(false)).Exit(0).Has("-k shop").Has("DESCRIBE TABLES").Has("temporary file")
                };
                foreach (SkillRunResult run in runs)
                {
                    MuxAssert.DoesNotContain(Secret, run.Stdout + run.Stderr, "no secret in the output");
                }

                (await c.Run(true, env, "nosql-mongodb", "find", "orders", "not json").ConfigureAwait(false)).Exit(2).Has("the filter must be JSON");
                (await c.Run(true, env, "nosql-mongodb", "find").ConfigureAwait(false)).Exit(2).Has("usage: nosql-mongodb find");
                (await c.Run(true, env, "nosql-dynamodb", "get", "Orders", "{bad").ConfigureAwait(false)).Exit(2).Has("DynamoDB JSON");
                (await c.Run(true, env, "nosql-cassandra", "query", "truncate shop.orders").ConfigureAwait(false)).Exit(2).Has("TRUNCATE is not one");
                (await c.Run(true, env, "nosql-cassandra", "describe", "shop.orders; drop").ConfigureAwait(false)).Exit(2).Has("plain table name");
            });

            Add("GraphDryRuns", "Neo4j runs in read access mode over stdin; LiteGraph builds REST calls, refuses write clauses outside strings, and validates GUIDs", async (SkillTestContext c) =>
            {
                string guid = "00000000-0000-0000-0000-000000000000";
                Dictionary<string, string> env = new Dictionary<string, string> { ["LITEGRAPH_API_KEY"] = Secret, ["LITEGRAPH_TENANT_GUID"] = guid, ["LITEGRAPH_GRAPH_GUID"] = guid, ["LITEGRAPH_ENDPOINT"] = "http://lg.local:8701/" };
                (await c.Run(true, env, "graph-neo4j", "query", "MATCH (n) RETURN count(n)").ConfigureAwait(false)).Exit(0).Has("cypher-shell --access-mode read").Has("DRYRUN input: MATCH (n) RETURN count(n);");
                SkillRunResult tenants = (await c.Run(true, env, "graph-litegraph", "tenants").ConfigureAwait(false)).Exit(0).Has("GET http://lg.local:8701/v1.0/tenants").Has("Bearer $LITEGRAPH_API_KEY");
                SkillRunResult nodes = (await c.Run(true, env, "graph-litegraph", "nodes", "--max", "5").ConfigureAwait(false)).Exit(0).Has("/graphs/" + guid + "/nodes?max-keys=5");
                SkillRunResult query = (await c.Run(true, env, "graph-litegraph", "query", "MATCH (n:Person) RETURN n LIMIT 5").ConfigureAwait(false)).Exit(0).Has("POST").Has("/query").Has("\"Query\":\"MATCH (n:Person) RETURN n LIMIT 5\"");
                (await c.Run(true, env, "graph-litegraph", "query", "MATCH (n {name:'SET'}) RETURN n").ConfigureAwait(false)).Exit(0);
                (await c.Run(true, env, "graph-litegraph", "query", "MATCH (n) SET n.x = 1 RETURN n").ConfigureAwait(false)).Exit(2).Has("SET changes the graph");
                (await c.Run(true, env, "graph-litegraph", "query", "CREATE (n:X)").ConfigureAwait(false)).Exit(2).Has("start with MATCH");
                (await c.Run(true, env, "graph-litegraph", "graphs", "--tenant", "nope").ConfigureAwait(false)).Exit(2).Has("must be a GUID");
                Dictionary<string, string> noTenant = new Dictionary<string, string> { ["LITEGRAPH_TENANT_GUID"] = string.Empty };
                (await c.Run(true, noTenant, "graph-litegraph", "graphs").ConfigureAwait(false)).Exit(2).Has("--tenant <guid>");
                foreach (SkillRunResult run in new[] { tenants, nodes, query })
                {
                    MuxAssert.DoesNotContain(Secret, run.Stdout + run.Stderr, "no token in the output");
                }
            });

            Add("LiteGraphAgainstServer", "graph-litegraph sends the bearer token, prints the JSON, and exits 1 on an HTTP error", async (SkillTestContext c) =>
            {
                using (StubHttpServer ok = new StubHttpServer(200, "application/json", "[{\"GUID\":\"00000000-0000-0000-0000-000000000000\",\"Name\":\"Default tenant\"}]"))
                {
                    Dictionary<string, string> env = new Dictionary<string, string> { ["LITEGRAPH_ENDPOINT"] = ok.BaseUrl, ["LITEGRAPH_API_KEY"] = Secret };
                    SkillRunResult run = (await c.Run(false, env, "graph-litegraph", "tenants").ConfigureAwait(false)).Exit(0).Has("Default tenant");
                    MuxAssert.DoesNotContain(Secret, run.Stdout + run.Stderr, "the token is not printed");
                    MuxAssert.AreEqual("Bearer " + Secret, ok.Requests.Last()["Authorization"], "the token was sent");
                }

                using (StubHttpServer denied = new StubHttpServer(401, "application/json", "{\"Error\":\"AuthenticationFailed\"}"))
                {
                    Dictionary<string, string> env = new Dictionary<string, string> { ["LITEGRAPH_ENDPOINT"] = denied.BaseUrl, ["LITEGRAPH_API_KEY"] = "wrong" };
                    (await c.Run(false, env, "graph-litegraph", "tenants").ConfigureAwait(false)).Exit(1).Has("AuthenticationFailed").Has("HTTP 401");
                }

                Dictionary<string, string> dead = new Dictionary<string, string> { ["LITEGRAPH_ENDPOINT"] = "http://127.0.0.1:" + StubHttpServer.FreeLoopbackPort(), ["LITEGRAPH_API_KEY"] = "x" };
                (await c.Run(false, dead, "graph-litegraph", "ping").ConfigureAwait(false)).Exit(2).Has("could not reach LiteGraph");
                Dictionary<string, string> noKey = new Dictionary<string, string> { ["LITEGRAPH_API_KEY"] = string.Empty, ["LITEGRAPH_TOKEN"] = string.Empty };
                (await c.Run(false, noKey, "graph-litegraph", "tenants").ConfigureAwait(false)).Exit(2).Has("set $LITEGRAPH_API_KEY");
            });

            Add("MigrationDetectionAndPlans", "db-migrate detects each framework, picks one with --tool, and plans without applying", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "src/Data/Data.csproj", "<Project />");
                Write(c, "src/Data/Migrations/AppDbContextModelSnapshot.cs", "// snapshot");
                Write(c, "alembic.ini", "[alembic]");
                Write(c, "manage.py", "# django");
                Write(c, "migrations/0001_init.up.sql", "create table t (id int);");
                Write(c, "migrations/0002_users.up.sql", "create table u (id int);");
                (await c.Run("db-migrate", "detect").ConfigureAwait(false)).Exit(0).Has("efcore: EF Core (src/Data/Data.csproj)").Has("alembic: Alembic").Has("django: Django").Has("golang-migrate: golang-migrate (migrations)");
                (await c.RunIn("src", true, "db-migrate", "status").ConfigureAwait(false)).Exit(0).Has("using efcore").Has("DRYRUN: dotnet ef migrations list --project src/Data/Data.csproj");
                (await c.Run(true, "db-migrate", "plan", "--tool", "alembic").ConfigureAwait(false)).Exit(0).Has("DRYRUN: alembic upgrade head --sql").Has("Nothing was applied");
                (await c.Run(true, "db-migrate", "plan", "--tool", "django").ConfigureAwait(false)).Exit(0).Has("manage.py migrate --plan");
                (await c.Run(false, "db-migrate", "plan", "--tool", "golang-migrate").ConfigureAwait(false)).Exit(0).Has("0001_init.up.sql").Has("0002_users.up.sql");
                (await c.Run(true, "db-migrate", "status", "--tool", "rails").ConfigureAwait(false)).Exit(2).Has("--tool rails was not detected here");
                (await c.Run(true, "db-migrate", "status", "--bogus").ConfigureAwait(false)).Exit(2).Has("unknown argument --bogus");
            });

            Add("MigrationApplyIsGuarded", "db-migrate apply refuses a production-looking target unless confirmed, and keeps the database URL secret", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "alembic.ini", "[alembic]");
                Write(c, "migrations/0001_init.up.sql", "create table t (id int);");
                Dictionary<string, string> prod = new Dictionary<string, string> { ["MUX_DB_ENVIRONMENT"] = "Production" };
                (await c.Run(true, prod, "db-migrate", "apply", "--tool", "alembic").ConfigureAwait(false)).Exit(3).Has("Target environment: Production").Has("looks like production");
                (await c.Run(true, prod, "db-migrate", "apply", "--tool", "alembic", "--confirm", "Production").ConfigureAwait(false)).Exit(0).Has("DRYRUN: alembic upgrade head");
                Dictionary<string, string> local = new Dictionary<string, string> { ["MUX_DB_ENVIRONMENT"] = "staging" };
                (await c.Run(true, local, "db-migrate", "apply", "--tool", "alembic").ConfigureAwait(false)).Exit(0).Has("Target environment: staging");
                Dictionary<string, string> url = new Dictionary<string, string>
                {
                    ["MUX_DB_ENVIRONMENT"] = string.Empty, ["ASPNETCORE_ENVIRONMENT"] = string.Empty, ["DOTNET_ENVIRONMENT"] = string.Empty, ["RAILS_ENV"] = string.Empty,
                    ["RACK_ENV"] = string.Empty, ["APP_ENV"] = string.Empty, ["NODE_ENV"] = string.Empty, ["FLASK_ENV"] = string.Empty, ["DJANGO_SETTINGS_MODULE"] = string.Empty,
                    ["DATABASE_URL"] = "postgres://u:" + Secret + "@prod-db.example.com/app"
                };
                SkillRunResult refused = (await c.Run(true, url, "db-migrate", "apply", "--tool", "golang-migrate").ConfigureAwait(false)).Exit(3).Has("prod-db.example.com");
                SkillRunResult status = (await c.Run(true, url, "db-migrate", "status", "--tool", "golang-migrate").ConfigureAwait(false)).Exit(0).Has("-database $DATABASE_URL version");
                MuxAssert.DoesNotContain(Secret, refused.Stdout + status.Stdout, "the URL's credentials are never shown");
                Dictionary<string, string> noUrl = new Dictionary<string, string> { ["DATABASE_URL"] = string.Empty, ["MUX_DB_ENVIRONMENT"] = "dev" };
                (await c.Run(false, noUrl, "db-migrate", "status", "--tool", "golang-migrate").ConfigureAwait(false)).Exit(2).Has("needs the database URL in $DATABASE_URL");
            });

            Add("NoMigrations", "db-migrate in a project without migrations exits 2 and names what it looked for", async (SkillTestContext c) =>
            {
                Write(c, "README.md", "# x");
                (await c.Run("db-migrate", "status").ConfigureAwait(false)).Exit(2).Has("no migrations found").Has("golang-migrate");
            });

            return new TestSuiteDescriptor(SuiteId, "Database skills: migrations and read-only SQL, NoSQL, and graph platforms", cases);
        }

        #endregion

        #region Private-Methods

        private static string Sqlite(string database, string sql)
        {
            ProcessStartInfo info = new ProcessStartInfo("sqlite3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(database);
            info.ArgumentList.Add(sql);
            using (Process process = Process.Start(info)!)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);
                return output;
            }
        }

        private static string Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static bool IsOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string candidate in new[] { executable, executable + ".exe", executable + ".cmd" })
                {
                    if (File.Exists(Path.Combine(directory, candidate))) return true;
                }
            }

            return false;
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-dbskills-" + Guid.NewGuid().ToString("N"));
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
