namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The database default skills. <c>db-migrate</c> finds the project's migration framework and shows status, plans,
    /// and (behind the production guard) applies migrations. One basic read-only skill per platform covers the major
    /// SQL databases (SQLite, PostgreSQL, MySQL and MariaDB, SQL Server, Oracle), NoSQL stores (MongoDB, Redis, DynamoDB,
    /// Cassandra), and graph databases (Neo4j, LiteGraph): connect, list, describe, and query. Connection strings come
    /// from named environment variables and are never shown, and each platform blocks writes in its own way.
    /// </summary>
    public static class DefaultDatabaseSkills
    {
        #region Private-Members

        private static readonly string[] _MigrationMarkers =
        {
            "alembic.ini", "manage.py", "**/schema.prisma", "db/migrate/*.rb", "flyway.conf", "flyway.toml", "conf/flyway.conf",
            "**/Migrations/*ModelSnapshot.cs", "migrations/*.up.sql", "db/migrations/*.up.sql"
        };

        private const string PlatformExitNote = " Exit codes: 0 success, 1 the database reported an error, 2 the client is missing, the connection variable is unset, or the input was refused.";

        private const string CommonSetup = @"function Get-MuxDbOptions {
    param([object[]]$Arguments, [string]$DefaultUrlEnv, [string[]]$Extra = @())
    $result = @{ UrlEnv = $DefaultUrlEnv; Limit = 200; Rest = (New-Object System.Collections.Generic.List[string]); Values = @{} }
    $list = @($Arguments)
    for ($i = 0; $i -lt $list.Count; $i++) {
        $token = [string]$list[$i]
        if ($token -eq '--url-env' -or $token -eq '--limit' -or $Extra -contains $token) {
            if (($i + 1) -ge $list.Count) { Exit-MuxNotApplicable ($token + ' needs a value.') }
            $value = [string]$list[$i + 1]
            $i++
            if ($token -eq '--url-env') { $result.UrlEnv = $value } elseif ($token -eq '--limit') { $result.Limit = Get-MuxLineLimit $value 200 } else { $result.Values[$token] = $value }
            continue
        }
        $result.Rest.Add($token)
    }
    if ($result.UrlEnv -and $result.UrlEnv -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Exit-MuxNotApplicable '--url-env takes the name of an environment variable (for example DATABASE_URL), never the connection string itself.' }
    return $result
}
function Get-MuxSecretFromEnv {
    param([string]$Name, [string]$What)
    $value = [Environment]::GetEnvironmentVariable($Name)
    if (-not $value -and -not (Test-MuxDryRun)) { Exit-MuxNotApplicable ('$' + $Name + ' is not set. Put the ' + $What + ' in it, or pass --url-env <VARIABLE>.') }
    return [string]$value
}
function Assert-MuxIdentifier {
    param([string]$Value, [string]$What)
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_$#]*(\.[A-Za-z_][A-Za-z0-9_$#]*)?$') { Exit-MuxNotApplicable ('pass a plain ' + $What + ' name (letters, digits, underscores, optionally schema.name), got ' + $Value) }
    return $Value
}
function Assert-MuxReadOnlySql {
    param([string]$Sql, [string[]]$Allowed)
    $text = [regex]::Replace([string]$Sql, '/\*.*?\*/', ' ', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $text = [regex]::Replace($text, '--[^\r\n]*', ' ')
    $first = ([regex]::Match($text.TrimStart(), '^[A-Za-z]+')).Value.ToLowerInvariant()
    if (-not $first) { Exit-MuxNotApplicable 'pass the statement to run.' }
    if ($Allowed -notcontains $first) { Exit-MuxNotApplicable ('only read statements run here (' + ($Allowed -join ', ') + '); ' + $first.ToUpperInvariant() + ' is not one. Use db-migrate for schema changes.') }
    $quoted = [regex]::Replace($text, '''([^'']|'''')*''|""([^""]|"""")*""', ""''"")
    if ($quoted.Trim().TrimEnd(';').Contains(';')) { Exit-MuxNotApplicable 'run one statement at a time.' }
    return ([string]$Sql).Trim().TrimEnd(';').Trim()
}
function Invoke-MuxDbCommand {
    param([string]$Exe, [string[]]$Arguments, [string[]]$Shown, [int]$Limit = 200, [string]$Hint = '', [string]$InputText = '', [string]$ShownInput = '', [scriptblock]$Cleanup = $null)
    $hasInput = $PSBoundParameters.ContainsKey('InputText')
    if (-not $PSBoundParameters.ContainsKey('ShownInput')) { $ShownInput = $InputText }
    $line = Format-MuxCommand -Tool $Exe -Arguments $Shown
    if (Test-MuxDryRun) {
        Write-Output ('DRYRUN: ' + $line)
        if ($hasInput) { Write-Output ('DRYRUN input: ' + $ShownInput) }
        if ($Cleanup) { & $Cleanup }
        exit 0
    }
    if (-not (Test-MuxTool $Exe)) { if ($Cleanup) { & $Cleanup }; Exit-MuxNotApplicable (""'"" + $Exe + ""' was not found on PATH. "" + $Hint) }
    Write-Output ('> ' + $line + '  (read-only)')
    if ($hasInput) { $output = @($InputText | & $Exe @Arguments 2>&1) } else { $output = @(& $Exe @Arguments 2>&1) }
    $code = $LASTEXITCODE
    if ($Cleanup) { & $Cleanup }
    $count = 0
    foreach ($row in $output) {
        if ($count -ge $Limit) { Write-Output ('[mux: output cut at ' + $Limit + ' lines; narrow the query or pass --limit]'); break }
        Write-Output ([string]$row)
        $count++
    }
    if ($code -ne 0) { exit 1 }
    exit 0
}
function Get-MuxSqlArgs {
    param([object[]]$Arguments, [string]$Usage, [int]$Count)
    $rest = @($Arguments)
    if ($rest.Count -lt $Count) { Exit-MuxNotApplicable ('usage: ' + $Usage) }
    return $rest
}
";

        private const string MigrateSetup = @"function Find-MuxFile {
    param([string]$Root, [string]$Filter, [int]$Depth = 3)
    return Get-ChildItem -LiteralPath $Root -Filter $Filter -File -Recurse -Depth $Depth -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](node_modules|bin|obj|\.git|\.venv|venv|vendor)[\\/]' } |
        Select-Object -First 1
}
function Get-MuxPythonFor {
    param([string]$Root)
    $venv = Get-MuxVenvPython $Root
    if ($venv) { return $venv }
    return Get-MuxSystemPython
}
function Get-MuxMigrationTools {
    param([string]$Root)
    $tools = New-Object System.Collections.Generic.List[object]
    $snapshot = Find-MuxFile $Root '*ModelSnapshot.cs' 5
    if ($snapshot) {
        $dir = $snapshot.Directory
        $project = $null
        while ($dir -and -not $project) {
            $project = Get-ChildItem -LiteralPath $dir.FullName -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($dir.FullName -eq $Root) { break }
            $dir = $dir.Parent
        }
        if ($project) {
            $p = [System.IO.Path]::GetRelativePath($Root, $project.FullName).Replace('\', '/')
            $tools.Add(@{ Name = 'efcore'; Label = 'EF Core (' + $p + ')'; Exe = 'dotnet'; Hint = 'Install the .NET SDK and dotnet-ef: dotnet tool install --global dotnet-ef.'
                Status = @('ef', 'migrations', 'list', '--project', $p); Plan = @('ef', 'migrations', 'script', '--idempotent', '--project', $p); Apply = @('ef', 'database', 'update', '--project', $p) })
        }
    }
    $prisma = Find-MuxFile $Root 'schema.prisma' 3
    if ($prisma) {
        $schema = [System.IO.Path]::GetRelativePath($Root, $prisma.FullName).Replace('\', '/')
        $tools.Add(@{ Name = 'prisma'; Label = 'Prisma (' + $schema + ')'; Exe = 'npx'; Hint = 'Install Node.js and the prisma package.'
            Status = @('prisma', 'migrate', 'status', '--schema', $schema); Plan = @('prisma', 'migrate', 'status', '--schema', $schema); Apply = @('prisma', 'migrate', 'deploy', '--schema', $schema)
            PlanNote = 'Prisma has no offline SQL preview for deploy; the pending migrations are listed above, and each one is a folder of SQL under prisma/migrations.' })
    }
    if (Test-Path -LiteralPath (Join-Path $Root 'alembic.ini')) {
        $tools.Add(@{ Name = 'alembic'; Label = 'Alembic'; Exe = 'alembic'; Hint = 'Install alembic in the project environment: pip install alembic.'
            Status = @('current', '--verbose'); Plan = @('upgrade', 'head', '--sql'); Apply = @('upgrade', 'head') })
    }
    if (Test-Path -LiteralPath (Join-Path $Root 'manage.py')) {
        $python = Get-MuxPythonFor $Root
        $tools.Add(@{ Name = 'django'; Label = 'Django'; Exe = $python; Hint = 'Install Python and the project dependencies.'
            Status = @('manage.py', 'showmigrations'); Plan = @('manage.py', 'migrate', '--plan'); Apply = @('manage.py', 'migrate') })
    }
    if ((Test-Path -LiteralPath (Join-Path $Root 'bin/rails')) -and (Test-Path -LiteralPath (Join-Path $Root 'db/migrate'))) {
        $rails = Join-Path $Root 'bin/rails'
        $tools.Add(@{ Name = 'rails'; Label = 'Rails'; Exe = $rails; Hint = 'Install Ruby and run bundle install.'
            Status = @('db:migrate:status'); Plan = @('db:migrate:status'); Apply = @('db:migrate')
            PlanNote = 'Rails has no dry run for db:migrate; the migrations marked down above are the ones that would run.' })
    }
    $flyway = @('flyway.toml', 'flyway.conf', 'conf/flyway.conf', 'conf/flyway.toml') | Where-Object { Test-Path -LiteralPath (Join-Path $Root $_) } | Select-Object -First 1
    if ($flyway) {
        $tools.Add(@{ Name = 'flyway'; Label = 'Flyway (' + $flyway + ')'; Exe = 'flyway'; Hint = 'Install Flyway from https://flywaydb.org.'
            Status = @('info'); Plan = @('validate'); Apply = @('migrate')
            PlanNote = 'flyway validate checks the applied migrations against the files; the pending ones are listed by status.' })
    }
    $sqlDir = @('migrations', 'db/migrations') | Where-Object { Get-ChildItem -LiteralPath (Join-Path $Root $_) -Filter '*.up.sql' -File -ErrorAction SilentlyContinue | Select-Object -First 1 } | Select-Object -First 1
    if ($sqlDir) {
        $tools.Add(@{ Name = 'golang-migrate'; Label = 'golang-migrate (' + $sqlDir + ')'; Exe = 'migrate'; Hint = 'Install golang-migrate from https://github.com/golang-migrate/migrate.'
            Status = @('-source', ('file://' + $sqlDir), '-database', '{URL}', 'version'); Plan = $null; Apply = @('-source', ('file://' + $sqlDir), '-database', '{URL}', 'up'); Dir = $sqlDir; NeedsUrl = $true })
    }
    return ,$tools
}
function Get-MuxMigrationArgs {
    param([object[]]$Arguments)
    $result = @{ Tool = ''; UrlEnv = 'DATABASE_URL'; Confirm = '' }
    $list = @($Arguments)
    for ($i = 0; $i -lt $list.Count; $i++) {
        $token = [string]$list[$i]
        if (@('--tool', '--url-env', '--confirm') -contains $token) {
            if (($i + 1) -ge $list.Count) { Exit-MuxNotApplicable ($token + ' needs a value.') }
            $value = [string]$list[$i + 1]
            $i++
            if ($token -eq '--tool') { $result.Tool = $value.ToLowerInvariant() } elseif ($token -eq '--url-env') { $result.UrlEnv = $value } else { $result.Confirm = $value }
            continue
        }
        Exit-MuxNotApplicable ('unknown argument ' + $token + '. Use --tool <name>, --url-env <VARIABLE>, or --confirm <environment>.')
    }
    if ($result.UrlEnv -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Exit-MuxNotApplicable ('--url-env takes an environment variable name, got ' + $result.UrlEnv) }
    return $result
}
function Select-MuxMigrationTool {
    param([string]$Root, [string]$Wanted)
    $tools = Get-MuxMigrationTools $Root
    if ($tools.Count -eq 0) { Exit-MuxNotApplicable 'no migrations found (EF Core, Prisma, Alembic, Django, Rails, Flyway, or golang-migrate).' }
    if ($Wanted) {
        $match = $tools | Where-Object { $_.Name -eq $Wanted } | Select-Object -First 1
        if (-not $match) { Exit-MuxNotApplicable ('--tool ' + $Wanted + ' was not detected here; found ' + (($tools | ForEach-Object { $_.Name }) -join ', ') + '.') }
        return $match
    }
    if ($tools.Count -gt 1) { Write-Host ('Found ' + (($tools | ForEach-Object { $_.Name }) -join ', ') + '; using ' + $tools[0].Name + '. Pass --tool <name> to pick another.') }
    return $tools[0]
}
function Invoke-MuxMigrationStep {
    param([hashtable]$Tool, [string[]]$StepArgs, [string]$UrlEnv)
    $shown = @($StepArgs | ForEach-Object { if ($_ -eq '{URL}') { '$' + $UrlEnv } else { $_ } })
    $actual = @($StepArgs)
    if ($Tool.NeedsUrl) {
        $url = [Environment]::GetEnvironmentVariable($UrlEnv)
        if (-not $url -and -not (Test-MuxDryRun)) { Exit-MuxNotApplicable ('golang-migrate needs the database URL in $' + $UrlEnv + ' (or pass --url-env <VARIABLE>).') }
        $actual = @($StepArgs | ForEach-Object { if ($_ -eq '{URL}') { $url } else { $_ } })
    }
    $line = Format-MuxCommand -Tool ([System.IO.Path]::GetFileName($Tool.Exe)) -Arguments $shown
    $script:MuxMigrationExit = 0
    if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + $line); return }
    if (-not (Test-MuxTool $Tool.Exe) -and -not (Test-Path -LiteralPath $Tool.Exe -PathType Leaf)) { Exit-MuxNotApplicable (""'"" + $Tool.Exe + ""' was not found. "" + $Tool.Hint) }
    Write-Output ('> ' + $line)
    & $Tool.Exe @actual
    $script:MuxMigrationExit = $LASTEXITCODE
}
function Get-MuxDatabaseEnvironment {
    param([string]$UrlEnv)
    foreach ($name in 'MUX_DB_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT', 'RAILS_ENV', 'RACK_ENV', 'APP_ENV', 'NODE_ENV', 'FLASK_ENV', 'DJANGO_SETTINGS_MODULE') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { return $value }
    }
    $url = [Environment]::GetEnvironmentVariable($UrlEnv)
    if ($url) {
        try { return ([System.Uri]$url).Host } catch { }
    }
    return 'local'
}
";

        private const string DetectCode = @"$root = Get-MuxRepoRoot
$tools = Get-MuxMigrationTools $root
if ($tools.Count -eq 0) { Exit-MuxNotApplicable 'no migrations found (EF Core, Prisma, Alembic, Django, Rails, Flyway, or golang-migrate).' }
foreach ($tool in $tools) { Write-Output ($tool.Name + ': ' + $tool.Label) }
";

        private const string StatusCode = @"$options = Get-MuxMigrationArgs $args
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
$tool = Select-MuxMigrationTool $root $options.Tool
Invoke-MuxMigrationStep $tool $tool.Status $options.UrlEnv
if ($script:MuxMigrationExit -ne 0) { exit 1 }
exit 0
";

        private const string PlanCode = @"$options = Get-MuxMigrationArgs $args
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
$tool = Select-MuxMigrationTool $root $options.Tool
if ($tool.Name -eq 'golang-migrate') {
    Write-Output ('Migration files in ' + $tool.Dir + ' (each runs in order on up):')
    Get-ChildItem -LiteralPath (Join-Path $root $tool.Dir) -Filter '*.up.sql' -File | Sort-Object Name | ForEach-Object { Write-Output ('  ' + $_.Name) }
    Write-Output 'The database version (status) shows how many have already been applied.'
    exit 0
}
Invoke-MuxMigrationStep $tool $tool.Plan $options.UrlEnv
if ($tool.PlanNote) { Write-Output $tool.PlanNote }
Write-Output 'Nothing was applied. Run apply only after the user has reviewed this plan.'
if ($script:MuxMigrationExit -ne 0) { exit 1 }
exit 0
";

        private const string ApplyCode = @"$options = Get-MuxMigrationArgs $args
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
$tool = Select-MuxMigrationTool $root $options.Tool
$environment = Get-MuxDatabaseEnvironment $options.UrlEnv
Write-Output ('Target environment: ' + $environment)
Assert-MuxNotProduction -Target $environment -Confirm $options.Confirm
Invoke-MuxMigrationStep $tool $tool.Apply $options.UrlEnv
if ($script:MuxMigrationExit -ne 0) { exit 1 }
exit 0
";

        private const string SqlSqliteCode = @"$o = Get-MuxDbOptions $args ''
$rest = @($o.Rest)
$hint = 'Install the sqlite3 command-line shell (https://sqlite.org/download.html).'
if ($rest.Count -lt 1) { Exit-MuxNotApplicable ('pass the database file: sql-sqlite ' + $command + ' <file>') }
$file = $rest[0]
if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath $file -PathType Leaf)) { Exit-MuxNotApplicable ('database file not found: ' + $file) }
switch ($command) {
    'ping' { $sql = 'select sqlite_version() as sqlite_version' }
    'tables' { $sql = ""select type, name from sqlite_master where type in ('table', 'view') and name not like 'sqlite_%' order by name"" }
    'describe' {
        if ($rest.Count -lt 2) { Exit-MuxNotApplicable 'usage: sql-sqlite describe <file> <table>' }
        $table = Assert-MuxIdentifier $rest[1] 'table'
        $sql = ""select name, type, """"notnull"""" as not_null, dflt_value as default_value, pk from pragma_table_info('"" + $table + ""')""
    }
    'query' {
        if ($rest.Count -lt 2) { Exit-MuxNotApplicable 'usage: sql-sqlite query <file> ""<sql>""' }
        $sql = Assert-MuxReadOnlySql (($rest | Select-Object -Skip 1) -join ' ') @('select', 'with', 'explain', 'values', 'pragma')
    }
}
$cli = @('-readonly', '-bail', '-header', '-column', $file)
Invoke-MuxDbCommand -Exe 'sqlite3' -Arguments $cli -Shown $cli -Limit $o.Limit -Hint $hint -InputText ($sql + ';')
";

        private const string SqlPostgresCode = @"$o = Get-MuxDbOptions $args 'DATABASE_URL'
$rest = @($o.Rest)
switch ($command) {
    'ping' { $sql = 'select version()' }
    'tables' { $sql = ""select table_schema, table_name, table_type from information_schema.tables where table_schema not in ('pg_catalog', 'information_schema') order by 1, 2"" }
    'describe' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-postgres describe <table | schema.table>' }
        $parts = (Assert-MuxIdentifier $rest[0] 'table').Split('.')
        $filter = if ($parts.Count -eq 2) { ""table_schema = '"" + $parts[0] + ""' and table_name = '"" + $parts[1] + ""'"" } else { ""table_name = '"" + $parts[0] + ""'"" }
        $sql = 'select table_schema, column_name, data_type, is_nullable, column_default from information_schema.columns where ' + $filter + ' order by table_schema, ordinal_position'
    }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-postgres query ""<sql>""' }
        $sql = Assert-MuxReadOnlySql ($rest -join ' ') @('select', 'with', 'show', 'explain', 'values', 'table')
    }
}
$url = Get-MuxSecretFromEnv $o.UrlEnv 'Postgres connection string (postgres://user:password@host:5432/database)'
$env:PGOPTIONS = (([string]$env:PGOPTIONS) + ' -c default_transaction_read_only=on').Trim()
$cli = @('--no-psqlrc', '--set', 'ON_ERROR_STOP=1', '--pset', 'footer=off', '--dbname', $url, '--command', $sql)
$shown = @('--no-psqlrc', '--set', 'ON_ERROR_STOP=1', '--pset', 'footer=off', '--dbname', ('$' + $o.UrlEnv), '--command', $sql)
Invoke-MuxDbCommand -Exe 'psql' -Arguments $cli -Shown $shown -Limit $o.Limit -Hint 'Install the PostgreSQL client (psql).'
";

        private const string SqlMysqlCode = @"$o = Get-MuxDbOptions $args 'DATABASE_URL'
$rest = @($o.Rest)
switch ($command) {
    'ping' { $sql = 'select version() as version' }
    'tables' { $sql = ""select table_schema, table_name, table_type from information_schema.tables where table_schema not in ('mysql', 'information_schema', 'performance_schema', 'sys') order by 1, 2"" }
    'describe' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-mysql describe <table | database.table>' }
        $parts = (Assert-MuxIdentifier $rest[0] 'table').Split('.')
        $filter = if ($parts.Count -eq 2) { ""table_schema = '"" + $parts[0] + ""' and table_name = '"" + $parts[1] + ""'"" } else { ""table_schema = database() and table_name = '"" + $parts[0] + ""'"" }
        $sql = 'select column_name, column_type, is_nullable, column_key, column_default from information_schema.columns where ' + $filter + ' order by ordinal_position'
    }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-mysql query ""<sql>""' }
        $sql = Assert-MuxReadOnlySql ($rest -join ' ') @('select', 'with', 'show', 'describe', 'desc', 'explain', 'table', 'values')
    }
}
$url = Get-MuxSecretFromEnv $o.UrlEnv 'MySQL or MariaDB URL (mysql://user:password@host:3306/database)'
$conn = @('--protocol=TCP')
$shownConn = @('--protocol=TCP')
if ($url) {
    try { $uri = [System.Uri]$url } catch { Exit-MuxNotApplicable ('$' + $o.UrlEnv + ' is not a URL like mysql://user:password@host:3306/database.') }
    $user = [System.Uri]::UnescapeDataString(($uri.UserInfo -split ':', 2)[0])
    $password = if ($uri.UserInfo.Contains(':')) { [System.Uri]::UnescapeDataString(($uri.UserInfo -split ':', 2)[1]) } else { '' }
    $port = if ($uri.Port -gt 0) { [string]$uri.Port } else { '3306' }
    $database = $uri.AbsolutePath.Trim('/')
    if ($password) { $env:MYSQL_PWD = $password }
    $conn += @('--host', $uri.Host, '--port', $port)
    $shownConn += @('--host', $uri.Host, '--port', $port)
    if ($user) { $conn += @('--user', $user); $shownConn += @('--user', $user) }
    if ($database) { $conn += @('--database', $database); $shownConn += @('--database', $database) }
}
$script = 'START TRANSACTION READ ONLY; ' + $sql + '; ROLLBACK;'
$exe = if (Test-MuxTool 'mysql') { 'mysql' } elseif (Test-MuxTool 'mariadb') { 'mariadb' } else { 'mysql' }
Invoke-MuxDbCommand -Exe $exe -Arguments ($conn + @('--table', '--execute', $script)) -Shown ($shownConn + @('--table', '--execute', $script)) -Limit $o.Limit -Hint 'Install the MySQL or MariaDB client.'
";

        private const string SqlSqlserverCode = @"$o = Get-MuxDbOptions $args 'SQLSERVER_CONNECTION_STRING'
$rest = @($o.Rest)
switch ($command) {
    'ping' { $sql = 'select @@version as version' }
    'tables' { $sql = 'select TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE from INFORMATION_SCHEMA.TABLES order by 1, 2' }
    'describe' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-sqlserver describe <table | schema.table>' }
        $parts = (Assert-MuxIdentifier $rest[0] 'table').Split('.')
        $filter = if ($parts.Count -eq 2) { ""TABLE_SCHEMA = '"" + $parts[0] + ""' and TABLE_NAME = '"" + $parts[1] + ""'"" } else { ""TABLE_NAME = '"" + $parts[0] + ""'"" }
        $sql = 'select TABLE_SCHEMA, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_DEFAULT from INFORMATION_SCHEMA.COLUMNS where ' + $filter + ' order by TABLE_SCHEMA, ORDINAL_POSITION'
    }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-sqlserver query ""<sql>""' }
        $sql = Assert-MuxReadOnlySql ($rest -join ' ') @('select', 'with')
    }
}
$text = Get-MuxSecretFromEnv $o.UrlEnv 'SQL Server connection string (Server=host,1433;Database=db;User Id=user;Password=password)'
$conn = @()
$shownConn = @()
if ($text) {
    $pairs = @{}
    foreach ($part in ($text -split ';')) {
        $kv = $part -split '=', 2
        if ($kv.Count -eq 2) { $pairs[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim() }
    }
    $server = @($pairs['server'], $pairs['data source'], $pairs['address'], $pairs['addr']) | Where-Object { $_ } | Select-Object -First 1
    if (-not $server) { Exit-MuxNotApplicable ('$' + $o.UrlEnv + ' has no Server= (or Data Source=) entry.') }
    $server = $server -replace '^tcp:', ''
    $database = @($pairs['database'], $pairs['initial catalog']) | Where-Object { $_ } | Select-Object -First 1
    $user = @($pairs['user id'], $pairs['uid'], $pairs['user']) | Where-Object { $_ } | Select-Object -First 1
    $password = @($pairs['password'], $pairs['pwd']) | Where-Object { $_ } | Select-Object -First 1
    $conn += @('-S', $server)
    $shownConn += @('-S', $server)
    if ($database) { $conn += @('-d', $database); $shownConn += @('-d', $database) }
    if ($user) {
        $conn += @('-U', $user)
        $shownConn += @('-U', $user)
        if ($password) { $env:SQLCMDPASSWORD = $password }
    } else {
        $conn += @('-E')
        $shownConn += @('-E')
    }
    if (@('true', 'yes') -contains ([string]$pairs['trustservercertificate']).ToLowerInvariant()) { $conn += @('-C'); $shownConn += @('-C') }
}
$script = 'SET NOCOUNT ON; BEGIN TRANSACTION; ' + $sql + '; ROLLBACK TRANSACTION;'
Invoke-MuxDbCommand -Exe 'sqlcmd' -Arguments ($conn + @('-b', '-W', '-s', '|', '-Q', $script)) -Shown ($shownConn + @('-b', '-W', '-s', '|', '-Q', $script)) -Limit $o.Limit -Hint 'Install sqlcmd (https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility).'
";

        private const string SqlOracleCode = @"$o = Get-MuxDbOptions $args 'ORACLE_CONNECT'
$rest = @($o.Rest)
switch ($command) {
    'ping' { $sql = ""select banner from v`$version where rownum = 1"" }
    'tables' { $sql = 'select table_name from user_tables order by table_name' }
    'describe' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-oracle describe <table | owner.table>' }
        $parts = (Assert-MuxIdentifier $rest[0] 'table').ToUpperInvariant().Split('.')
        $sql = if ($parts.Count -eq 2) { ""select column_name, data_type, data_length, nullable from all_tab_columns where owner = '"" + $parts[0] + ""' and table_name = '"" + $parts[1] + ""' order by column_id"" } else { ""select column_name, data_type, data_length, nullable from user_tab_columns where table_name = '"" + $parts[0] + ""' order by column_id"" }
    }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: sql-oracle query ""<sql>""' }
        $sql = Assert-MuxReadOnlySql ($rest -join ' ') @('select', 'with')
    }
}
$connect = Get-MuxSecretFromEnv $o.UrlEnv 'Oracle connect string (user/password@//host:1521/service)'
$exe = if (Test-MuxTool 'sqlplus') { 'sqlplus' } elseif (Test-MuxTool 'sql') { 'sql' } else { 'sqlplus' }
$body = ""SET PAGESIZE 200 LINESIZE 250 FEEDBACK OFF TRIMSPOOL ON`nWHENEVER SQLERROR EXIT 1`nWHENEVER OSERROR EXIT 1`n""
$tail = ""SET TRANSACTION READ ONLY;`n"" + $sql + "";`nROLLBACK;`nEXIT`n""
Invoke-MuxDbCommand -Exe $exe -Arguments @('-S', '-L', '/nolog') -Shown @('-S', '-L', '/nolog') -Limit $o.Limit -Hint 'Install SQL*Plus (Oracle Instant Client) or SQLcl.' -InputText ($body + 'CONNECT ' + $connect + ""`n"" + $tail) -ShownInput ($body + 'CONNECT $' + $o.UrlEnv + ""`n"" + $tail)
";

        private const string NosqlMongodbCode = @"$o = Get-MuxDbOptions $args 'MONGODB_URI' @('--docs')
$rest = @($o.Rest)
$docs = 20
if ($o.Values.ContainsKey('--docs')) { $docs = [Math]::Min(500, (Get-MuxLineLimit $o.Values['--docs'] 20)) }
function ConvertTo-MuxJsString { param([string]$Value) return ($Value | ConvertTo-Json -Compress) }
function Get-MuxMongoFilter {
    param([string]$Text)
    if (-not $Text) { return '{}' }
    try { $null = $Text | ConvertFrom-Json -Depth 32 } catch { Exit-MuxNotApplicable ('the filter must be JSON, for example {""status"":""active""}; got ' + $Text) }
    return $Text
}
switch ($command) {
    'ping' { $js = 'printjson(db.runCommand({ ping: 1 })); print(''server version: '' + db.version()); print(''database: '' + db.getName());' }
    'collections' { $js = 'db.getCollectionNames().sort().forEach(function (n) { print(n); });' }
    'databases' { $js = 'db.adminCommand({ listDatabases: 1, nameOnly: true }).databases.forEach(function (d) { print(d.name); });' }
    'find' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-mongodb find <collection> [filter-json] [--docs n]' }
        $filter = Get-MuxMongoFilter (($rest | Select-Object -Skip 1) -join ' ')
        $js = 'printjson(db.getCollection(' + (ConvertTo-MuxJsString $rest[0]) + ').find(EJSON.parse(' + (ConvertTo-MuxJsString $filter) + ')).limit(' + $docs + ').toArray());'
    }
    'count' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-mongodb count <collection> [filter-json]' }
        $filter = Get-MuxMongoFilter (($rest | Select-Object -Skip 1) -join ' ')
        $js = 'print(db.getCollection(' + (ConvertTo-MuxJsString $rest[0]) + ').countDocuments(EJSON.parse(' + (ConvertTo-MuxJsString $filter) + ')));'
    }
    'indexes' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-mongodb indexes <collection>' }
        $js = 'printjson(db.getCollection(' + (ConvertTo-MuxJsString $rest[0]) + ').getIndexes());'
    }
}
$null = Get-MuxSecretFromEnv $o.UrlEnv 'MongoDB connection string (mongodb://user:password@host:27017/database)'
$script = 'const db = connect(process.env.' + $o.UrlEnv + '); ' + $js
$cli = @('--nodb', '--quiet', '--eval', $script)
Invoke-MuxDbCommand -Exe 'mongosh' -Arguments $cli -Shown $cli -Limit $o.Limit -Hint 'Install mongosh (https://www.mongodb.com/try/download/shell).'
";

        private const string NosqlRedisCode = @"$o = Get-MuxDbOptions $args 'REDIS_URL'
$rest = @($o.Rest)
$url = [Environment]::GetEnvironmentVariable($o.UrlEnv)
if (-not $url) { $url = 'redis://127.0.0.1:6379' }
try { $uri = [System.Uri]$url } catch { Exit-MuxNotApplicable ('$' + $o.UrlEnv + ' is not a URL like redis://:password@host:6379/0.') }
$conn = @('-h', $uri.Host, '-p', $(if ($uri.Port -gt 0) { [string]$uri.Port } else { '6379' }), '--no-auth-warning')
$database = $uri.AbsolutePath.Trim('/')
if ($database -match '^\d+$') { $conn += @('-n', $database) }
if ($uri.Scheme -eq 'rediss') { $conn += @('--tls') }
if ($uri.UserInfo) {
    $parts = $uri.UserInfo -split ':', 2
    $user = [System.Uri]::UnescapeDataString($parts[0])
    if ($parts.Count -eq 2) { $env:REDISCLI_AUTH = [System.Uri]::UnescapeDataString($parts[1]) }
    if ($user -and $user -ne 'default') { $conn += @('--user', $user) }
}
$hint = 'Install redis-cli (part of Redis).'
switch ($command) {
    'ping' { $cmd = @('INFO', 'server') }
    'info' { $cmd = @('INFO', $(if ($rest.Count -gt 0) { $rest[0] } else { 'keyspace' })) }
    'keys' { $cmd = @('--scan', '--count', '500', '--pattern', $(if ($rest.Count -gt 0) { $rest[0] } else { '*' })) }
    'get' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-redis get <key>' }
        $key = $rest[0]
        $type = 'string'
        if (-not (Test-MuxDryRun)) {
            if (-not (Test-MuxTool 'redis-cli')) { Exit-MuxNotApplicable (""'redis-cli' was not found on PATH. "" + $hint) }
            $type = ([string](& redis-cli @conn TYPE $key 2>&1)).Trim()
            if ($LASTEXITCODE -ne 0) { Write-Output $type; exit 1 }
            Write-Output ('type: ' + $type)
        }
        switch ($type) {
            'string' { $cmd = @('GET', $key) }
            'hash' { $cmd = @('HGETALL', $key) }
            'list' { $cmd = @('LRANGE', $key, '0', '99') }
            'set' { $cmd = @('SSCAN', $key, '0', 'COUNT', '100') }
            'zset' { $cmd = @('ZRANGE', $key, '0', '99', 'WITHSCORES') }
            'stream' { $cmd = @('XRANGE', $key, '-', '+', 'COUNT', '20') }
            'none' { Write-Output ('No key named ' + $key + '.'); exit 1 }
            default { $cmd = @('TYPE', $key) }
        }
    }
}
Invoke-MuxDbCommand -Exe 'redis-cli' -Arguments ($conn + $cmd) -Shown ($conn + $cmd) -Limit $o.Limit -Hint $hint
";

        private const string NosqlDynamodbCode = @"$o = Get-MuxDbOptions $args '' @('--region', '--profile', '--items')
$rest = @($o.Rest)
$common = @('--output', 'json')
if ($o.Values.ContainsKey('--region')) { $common += @('--region', $o.Values['--region']) }
if ($o.Values.ContainsKey('--profile')) { $common += @('--profile', $o.Values['--profile']) }
$items = 20
if ($o.Values.ContainsKey('--items')) { $items = [Math]::Min(500, (Get-MuxLineLimit $o.Values['--items'] 20)) }
switch ($command) {
    'tables' { $cmd = @('dynamodb', 'list-tables') }
    'describe' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-dynamodb describe <table>' }
        $cmd = @('dynamodb', 'describe-table', '--table-name', $rest[0], '--query', 'Table.{Name:TableName,Status:TableStatus,Items:ItemCount,Bytes:TableSizeBytes,Keys:KeySchema,Attributes:AttributeDefinitions,Indexes:GlobalSecondaryIndexes[].IndexName,Billing:BillingModeSummary.BillingMode}')
    }
    'scan' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-dynamodb scan <table> [--items n]' }
        $cmd = @('dynamodb', 'scan', '--table-name', $rest[0], '--max-items', [string]$items)
    }
    'get' {
        if ($rest.Count -lt 2) { Exit-MuxNotApplicable 'usage: nosql-dynamodb get <table> <key-json>, for example {""id"":{""S"":""42""}}' }
        $key = ($rest | Select-Object -Skip 1) -join ' '
        try { $null = $key | ConvertFrom-Json } catch { Exit-MuxNotApplicable ('the key must be DynamoDB JSON, for example {""id"":{""S"":""42""}}; got ' + $key) }
        $cmd = @('dynamodb', 'get-item', '--table-name', $rest[0], '--key', $key)
    }
}
Invoke-MuxDbCommand -Exe 'aws' -Arguments ($cmd + $common) -Shown ($cmd + $common) -Limit $o.Limit -Hint 'Install the AWS CLI and sign in (aws configure or aws sso login).'
";

        private const string NosqlCassandraCode = @"$o = Get-MuxDbOptions $args ''
$rest = @($o.Rest)
switch ($command) {
    'keyspaces' { $cql = 'DESCRIBE KEYSPACES' }
    'tables' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-cassandra tables <keyspace>' }
        $cql = 'DESCRIBE TABLES'
        $keyspace = Assert-MuxIdentifier $rest[0] 'keyspace'
    }
    'describe' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-cassandra describe <keyspace.table>' }
        $cql = 'DESCRIBE TABLE ' + (Assert-MuxIdentifier $rest[0] 'table')
    }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: nosql-cassandra query ""SELECT ...""' }
        $cql = Assert-MuxReadOnlySql ($rest -join ' ') @('select')
    }
}
$hostName = if ($env:CASSANDRA_HOST) { $env:CASSANDRA_HOST } else { '127.0.0.1' }
$port = if ($env:CASSANDRA_PORT) { $env:CASSANDRA_PORT } else { '9042' }
$cli = @($hostName, $port)
$rc = $null
if ($env:CASSANDRA_USERNAME -and -not (Test-MuxDryRun)) {
    $rc = Join-Path ([System.IO.Path]::GetTempPath()) ('mux-cqlshrc-' + [guid]::NewGuid().ToString('N'))
    Set-Content -LiteralPath $rc -Value (""[authentication]`nusername = "" + $env:CASSANDRA_USERNAME + ""`npassword = "" + $env:CASSANDRA_PASSWORD + ""`n"")
    $cli += @('--cqlshrc', $rc)
}
$shownCli = @($hostName, $port)
if ($env:CASSANDRA_USERNAME) { $shownCli += @('--cqlshrc', '<temporary file with $CASSANDRA_USERNAME and $CASSANDRA_PASSWORD>') }
if ($keyspace) { $cli += @('-k', $keyspace); $shownCli += @('-k', $keyspace) }
$cli += @('-e', $cql)
$shownCli += @('-e', $cql)
$cleanup = { if ($rc -and (Test-Path -LiteralPath $rc)) { Remove-Item -LiteralPath $rc -Force } }
Invoke-MuxDbCommand -Exe 'cqlsh' -Arguments $cli -Shown $shownCli -Limit $o.Limit -Hint 'Install cqlsh (pip install cqlsh).' -Cleanup $cleanup
";

        private const string GraphNeo4jCode = @"$o = Get-MuxDbOptions $args ''
$rest = @($o.Rest)
switch ($command) {
    'ping' { $cypher = 'CALL dbms.components() YIELD name, versions, edition RETURN name, versions, edition' }
    'labels' { $cypher = 'CALL db.labels() YIELD label RETURN label ORDER BY label' }
    'relationships' { $cypher = 'CALL db.relationshipTypes() YIELD relationshipType RETURN relationshipType ORDER BY relationshipType' }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: graph-neo4j query ""MATCH ... RETURN ...""' }
        $cypher = ($rest -join ' ').Trim().TrimEnd(';')
    }
}
$cli = @('--access-mode', 'read', '--format', 'plain', '--non-interactive')
Invoke-MuxDbCommand -Exe 'cypher-shell' -Arguments $cli -Shown $cli -Limit $o.Limit -Hint 'Install cypher-shell (part of Neo4j) and set NEO4J_URI, NEO4J_USERNAME, and NEO4J_PASSWORD.' -InputText ($cypher + ';')
";

        private const string GraphLitegraphCode = @"$o = Get-MuxDbOptions $args '' @('--tenant', '--graph', '--max')
$rest = @($o.Rest)
$endpoint = if ($env:LITEGRAPH_ENDPOINT) { $env:LITEGRAPH_ENDPOINT.TrimEnd('/') } else { 'http://localhost:8701' }
$tenant = if ($o.Values.ContainsKey('--tenant')) { $o.Values['--tenant'] } elseif ($env:LITEGRAPH_TENANT_GUID) { $env:LITEGRAPH_TENANT_GUID } else { '' }
$graph = if ($o.Values.ContainsKey('--graph')) { $o.Values['--graph'] } elseif ($env:LITEGRAPH_GRAPH_GUID) { $env:LITEGRAPH_GRAPH_GUID } else { '' }
$max = 100
if ($o.Values.ContainsKey('--max')) { $max = [Math]::Min(1000, (Get-MuxLineLimit $o.Values['--max'] 100)) }
function Assert-MuxGuid {
    param([string]$Value, [string]$What, [string]$Variable)
    $parsed = [guid]::Empty
    if (-not $Value) { Exit-MuxNotApplicable ('pass --' + $What + ' <guid> or set $' + $Variable + '.') }
    if (-not [guid]::TryParse($Value, [ref]$parsed)) { Exit-MuxNotApplicable ('the ' + $What + ' must be a GUID, got ' + $Value) }
    return $Value
}
$method = 'GET'
$body = $null
$auth = $true
switch ($command) {
    'ping' { $path = '/v1.0/health/ready'; $auth = $false }
    'tenants' { $path = '/v1.0/tenants' }
    'graphs' { $path = '/v1.0/tenants/' + (Assert-MuxGuid $tenant 'tenant' 'LITEGRAPH_TENANT_GUID') + '/graphs' }
    'stats' {
        $t = Assert-MuxGuid $tenant 'tenant' 'LITEGRAPH_TENANT_GUID'
        $path = if ($graph) { '/v1.0/tenants/' + $t + '/graphs/' + (Assert-MuxGuid $graph 'graph' 'LITEGRAPH_GRAPH_GUID') + '/stats' } else { '/v1.0/tenants/' + $t + '/stats' }
    }
    'nodes' { $path = '/v1.0/tenants/' + (Assert-MuxGuid $tenant 'tenant' 'LITEGRAPH_TENANT_GUID') + '/graphs/' + (Assert-MuxGuid $graph 'graph' 'LITEGRAPH_GRAPH_GUID') + '/nodes?max-keys=' + $max }
    'edges' { $path = '/v1.0/tenants/' + (Assert-MuxGuid $tenant 'tenant' 'LITEGRAPH_TENANT_GUID') + '/graphs/' + (Assert-MuxGuid $graph 'graph' 'LITEGRAPH_GRAPH_GUID') + '/edges?max-keys=' + $max }
    'query' {
        if ($rest.Count -lt 1) { Exit-MuxNotApplicable 'usage: graph-litegraph query ""MATCH (n) RETURN n LIMIT 10"" [--tenant guid] [--graph guid]' }
        $query = ($rest -join ' ').Trim().TrimEnd(';')
        $unquoted = [regex]::Replace($query, '''([^''\\]|\\.)*''|""([^""\\]|\\.)*""', ""''"")
        if ($unquoted -notmatch '^\s*(OPTIONAL\s+)?MATCH\b') { Exit-MuxNotApplicable 'only read queries run here: start with MATCH and end with RETURN.' }
        $write = [regex]::Match($unquoted, '\b(CREATE|MERGE|SET|DELETE|DETACH|REMOVE|DROP)\b', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($write.Success) { Exit-MuxNotApplicable ($write.Value.ToUpperInvariant() + ' changes the graph; only read queries (MATCH ... RETURN) run here.') }
        $path = '/v1.0/tenants/' + (Assert-MuxGuid $tenant 'tenant' 'LITEGRAPH_TENANT_GUID') + '/graphs/' + (Assert-MuxGuid $graph 'graph' 'LITEGRAPH_GRAPH_GUID') + '/query'
        $method = 'POST'
        $body = @{ Query = $query; MaxResults = $max; TimeoutSeconds = 30 } | ConvertTo-Json -Compress
    }
}
$token = if ($env:LITEGRAPH_API_KEY) { $env:LITEGRAPH_API_KEY } else { $env:LITEGRAPH_TOKEN }
$line = $method + ' ' + $endpoint + $path
if ($auth) { $line += '  (Authorization: Bearer $LITEGRAPH_API_KEY)' }
if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + $line); if ($body) { Write-Output ('DRYRUN body: ' + $body) }; exit 0 }
if ($auth -and -not $token) { Exit-MuxNotApplicable 'set $LITEGRAPH_API_KEY to a LiteGraph bearer token (a read-scoped credential is best).' }
$headers = @{}
if ($auth) { $headers['Authorization'] = 'Bearer ' + $token }
Write-Output ('> ' + $line)
try {
    $response = Invoke-WebRequest -Uri ($endpoint + $path) -Method $method -Headers $headers -Body $body -ContentType 'application/json' -SkipHttpErrorCheck -TimeoutSec 30 -ErrorAction Stop
} catch {
    Write-Output ('mux: could not reach LiteGraph at ' + $endpoint + ': ' + $_.Exception.Message)
    exit 2
}
$text = [string]$response.Content
try { $text = ($text | ConvertFrom-Json -Depth 64) | ConvertTo-Json -Depth 64 } catch { }
$count = 0
foreach ($row in ($text -split ""`n"")) {
    if ($count -ge $o.Limit) { Write-Output ('[mux: output cut at ' + $o.Limit + ' lines; narrow the query or pass --limit]'); break }
    Write-Output $row.TrimEnd()
    $count++
}
if ([int]$response.StatusCode -ge 400) { Write-Output ('HTTP ' + [int]$response.StatusCode); exit 1 }
exit 0
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the database skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory migrate = new ToolchainSkillFactory(MigrateSetup, new[] { "data", "database", "sql" }, _MigrationMarkers, null, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                migrate.Skill("db-migrate", "Plan and apply database migrations",
                    "Shows database migration status, previews pending schema migrations as SQL, and applies them, for EF Core, Prisma, Alembic, Django, Rails, Flyway, or golang-migrate.",
                    true,
                    "The user asks about database migrations or schema changes: which are pending, what SQL they would run, or to apply them.",
                    "[--tool name] [--url-env VARIABLE] [--confirm environment]",
                    "`detect` lists the migration frameworks found under the repository root (EF Core from a model snapshot, Prisma from schema.prisma, Alembic from alembic.ini, Django from manage.py, Rails from bin/rails and db/migrate, Flyway from its config, golang-migrate from *.up.sql files). When several are found the first is used; `--tool <name>` picks another. `status` shows applied and pending migrations. `plan` previews what apply would do without changing anything: the idempotent SQL script for EF Core, `alembic upgrade head --sql`, `manage.py migrate --plan`, `flyway validate`, the pending list for Prisma and Rails, or the migration files for golang-migrate. `apply` runs the migrations; it is refused (exit 3) when the target environment looks like production unless `--confirm <environment>` repeats it. The target is the first of MUX_DB_ENVIRONMENT, ASPNETCORE_ENVIRONMENT, DOTNET_ENVIRONMENT, RAILS_ENV, RACK_ENV, APP_ENV, NODE_ENV, FLASK_ENV, and DJANGO_SETTINGS_MODULE, then the host of the database URL, then `local`. golang-migrate reads its URL from $DATABASE_URL (or `--url-env`), and the command line shows the variable name, never the URL. Always show the plan to the user before apply.",
                    ToolchainSkillFactory.Command("detect", "List the migration frameworks found.", DetectCode),
                    ToolchainSkillFactory.Command("status", "Show applied and pending migrations.", StatusCode),
                    ToolchainSkillFactory.Command("plan", "Preview pending migrations without applying them.", PlanCode),
                    ToolchainSkillFactory.Command("apply", "Apply pending migrations (production guarded).", ApplyCode)),

                Platform("sql-sqlite", "Query a SQLite database (read-only)",
                    "SQLite: shows the version, lists tables, describes a table, or runs a read-only SQL query against a database file.",
                    "The user asks to look inside a SQLite database file: its tables, columns, or rows.",
                    "<command> <file> [table | \"<sql>\"] [--limit lines]",
                    "Every command takes the database file first. `ping <file>` prints the SQLite version, `tables <file>` lists tables and views, `describe <file> <table>` lists its columns, and `query <file> \"<sql>\"` runs one SELECT, WITH, EXPLAIN, VALUES, or PRAGMA statement. The file is opened with `sqlite3 -readonly`, so nothing can be written.",
                    new[] { "*.db", "*.sqlite", "*.sqlite3", "**/*.db", "**/*.sqlite", "**/*.sqlite3" }, new[] { "sqlite3" },
                        Command("ping", "Print the SQLite version.", "$command = 'ping'\n" + SqlSqliteCode),
                        Command("tables", "List tables and views.", "$command = 'tables'\n" + SqlSqliteCode),
                        Command("describe", "List a table's columns.", "$command = 'describe'\n" + SqlSqliteCode),
                        Command("query", "Run one read-only statement.", "$command = 'query'\n" + SqlSqliteCode)),

                Platform("sql-postgres", "Query PostgreSQL (read-only)",
                    "PostgreSQL: shows the server version, lists tables, describes a table, or runs a read-only SQL query.",
                    "The user asks to inspect a Postgres database: its tables, columns, or data.",
                    "<command> [table | \"<sql>\"] [--url-env VARIABLE] [--limit lines]",
                    "The connection string comes from `$DATABASE_URL` (or the variable named by `--url-env`), and only the variable name is ever shown. `ping` prints `version()`, `tables` lists tables and views outside the system schemas, `describe <table | schema.table>` lists columns, and `query \"<sql>\"` runs one SELECT, WITH, SHOW, EXPLAIN, VALUES, or TABLE statement. The session runs with `default_transaction_read_only=on`, so the server rejects writes.",
                    null, new[] { "psql" },
                        Command("ping", "Print the server version.", "$command = 'ping'\n" + SqlPostgresCode),
                        Command("tables", "List tables and views.", "$command = 'tables'\n" + SqlPostgresCode),
                        Command("describe", "List a table's columns.", "$command = 'describe'\n" + SqlPostgresCode),
                        Command("query", "Run one read-only statement.", "$command = 'query'\n" + SqlPostgresCode)),

                Platform("sql-mysql", "Query MySQL or MariaDB (read-only)",
                    "MySQL and MariaDB: shows the server version, lists tables, describes a table, or runs a read-only SQL query.",
                    "The user asks to inspect a MySQL or MariaDB database: its tables, columns, or data.",
                    "<command> [table | \"<sql>\"] [--url-env VARIABLE] [--limit lines]",
                    "The connection is a URL like `mysql://user:password@host:3306/database` in `$DATABASE_URL` (or `--url-env`); the password is passed through MYSQL_PWD and never shown. `ping` prints the version, `tables` lists user tables, `describe <table | database.table>` lists columns, and `query \"<sql>\"` runs one SELECT, WITH, SHOW, DESCRIBE, EXPLAIN, TABLE, or VALUES statement inside `START TRANSACTION READ ONLY`, rolled back afterwards.",
                    null, new[] { "mysql|mariadb" },
                        Command("ping", "Print the server version.", "$command = 'ping'\n" + SqlMysqlCode),
                        Command("tables", "List tables.", "$command = 'tables'\n" + SqlMysqlCode),
                        Command("describe", "List a table's columns.", "$command = 'describe'\n" + SqlMysqlCode),
                        Command("query", "Run one read-only statement.", "$command = 'query'\n" + SqlMysqlCode)),

                Platform("sql-sqlserver", "Query SQL Server (read-only)",
                    "Microsoft SQL Server and Azure SQL: shows the server version, lists tables, describes a table, or runs a read-only T-SQL query.",
                    "The user asks to inspect a SQL Server or Azure SQL database: its tables, columns, or data.",
                    "<command> [table | \"<sql>\"] [--url-env VARIABLE] [--limit lines]",
                    "The connection string (`Server=host,1433;Database=db;User Id=user;Password=password`, or no user for Windows authentication) comes from `$SQLSERVER_CONNECTION_STRING` (or `--url-env`); the password goes through SQLCMDPASSWORD and is never shown. `ping` prints `@@version`, `tables` lists INFORMATION_SCHEMA tables, `describe <table | schema.table>` lists columns, and `query \"<sql>\"` runs one SELECT or WITH statement inside a transaction that is always rolled back.",
                    null, new[] { "sqlcmd" },
                        Command("ping", "Print the server version.", "$command = 'ping'\n" + SqlSqlserverCode),
                        Command("tables", "List tables.", "$command = 'tables'\n" + SqlSqlserverCode),
                        Command("describe", "List a table's columns.", "$command = 'describe'\n" + SqlSqlserverCode),
                        Command("query", "Run one read-only statement.", "$command = 'query'\n" + SqlSqlserverCode)),

                Platform("sql-oracle", "Query Oracle Database (read-only)",
                    "Oracle Database: shows the version, lists your tables, describes a table, or runs a read-only SQL query.",
                    "The user asks to inspect an Oracle database: its tables, columns, or data.",
                    "<command> [table | \"<sql>\"] [--url-env VARIABLE] [--limit lines]",
                    "The connect string (`user/password@//host:1521/service`) comes from `$ORACLE_CONNECT` (or `--url-env`) and is sent to SQL*Plus (or SQLcl) on standard input, never on the command line. `ping` prints the version banner, `tables` lists your tables, `describe <table | owner.table>` lists columns, and `query \"<sql>\"` runs one SELECT or WITH statement after `SET TRANSACTION READ ONLY`.",
                    null, new[] { "sqlplus|sql" },
                        Command("ping", "Print the version banner.", "$command = 'ping'\n" + SqlOracleCode),
                        Command("tables", "List your tables.", "$command = 'tables'\n" + SqlOracleCode),
                        Command("describe", "List a table's columns.", "$command = 'describe'\n" + SqlOracleCode),
                        Command("query", "Run one read-only statement.", "$command = 'query'\n" + SqlOracleCode)),

                Platform("nosql-mongodb", "Inspect MongoDB (read-only)",
                    "MongoDB: pings the server, lists databases and collections, finds or counts documents with a JSON filter, and shows indexes.",
                    "The user asks to look at MongoDB data: collections, documents, counts, or indexes.",
                    "<command> [collection] [filter-json] [--docs n] [--url-env VARIABLE]",
                    "The connection string comes from `$MONGODB_URI` (or `--url-env`) and is read by mongosh from the environment, never placed on the command line. `ping` prints the server version and database, `databases` and `collections` list names, `find <collection> [filter]` prints up to 20 documents (`--docs` up to 500), `count <collection> [filter]` counts matches, and `indexes <collection>` lists indexes. Filters are JSON (Extended JSON), for example `{\"status\":\"open\"}`. Only these reads are offered.",
                    null, new[] { "mongosh" },
                        Command("ping", "Ping the server and print its version.", "$command = 'ping'\n" + NosqlMongodbCode),
                        Command("databases", "List databases.", "$command = 'databases'\n" + NosqlMongodbCode),
                        Command("collections", "List collections in the database.", "$command = 'collections'\n" + NosqlMongodbCode),
                        Command("find", "Find documents with an optional JSON filter.", "$command = 'find'\n" + NosqlMongodbCode),
                        Command("count", "Count documents with an optional JSON filter.", "$command = 'count'\n" + NosqlMongodbCode),
                        Command("indexes", "List a collection's indexes.", "$command = 'indexes'\n" + NosqlMongodbCode)),

                Platform("nosql-redis", "Inspect Redis (read-only)",
                    "Redis: shows server info, scans keys by pattern, and reads a key whatever its type (string, hash, list, set, sorted set, or stream).",
                    "The user asks to look at Redis or Valkey data: keys, values, or server info.",
                    "<command> [pattern | key | section] [--url-env VARIABLE]",
                    "The connection is a URL like `redis://:password@host:6379/0` (`rediss://` for TLS) in `$REDIS_URL` (or `--url-env`), defaulting to redis://127.0.0.1:6379; the password goes through REDISCLI_AUTH and is never shown. `ping` prints INFO server, `info [section]` another INFO section (default keyspace), `keys [pattern]` iterates with SCAN (never KEYS), and `get <key>` reads it by type (the first 100 entries of a collection, 20 of a stream). Only these reads are offered.",
                    null, new[] { "redis-cli" },
                        Command("ping", "Show server info.", "$command = 'ping'\n" + NosqlRedisCode),
                        Command("info", "Show an INFO section (default keyspace).", "$command = 'info'\n" + NosqlRedisCode),
                        Command("keys", "Scan keys matching a pattern.", "$command = 'keys'\n" + NosqlRedisCode),
                        Command("get", "Read a key according to its type.", "$command = 'get'\n" + NosqlRedisCode)),

                Platform("nosql-dynamodb", "Inspect DynamoDB (read-only)",
                    "Amazon DynamoDB: lists tables, describes a table's keys and indexes, scans a few items, or gets one item by key.",
                    "The user asks to look at DynamoDB tables or items.",
                    "<command> [table] [key-json] [--items n] [--region r] [--profile p]",
                    "Uses the AWS CLI with your current credentials (`--region` and `--profile` override). `tables` lists tables, `describe <table>` shows status, item count, size, key schema, attributes, and index names, `scan <table>` returns up to 20 items (`--items` up to 500), and `get <table> <key-json>` reads one item, for example `{\"id\":{\"S\":\"42\"}}`. Only these reads are offered.",
                    null, new[] { "aws" },
                        Command("tables", "List tables.", "$command = 'tables'\n" + NosqlDynamodbCode),
                        Command("describe", "Describe a table's keys and indexes.", "$command = 'describe'\n" + NosqlDynamodbCode),
                        Command("scan", "Scan a few items.", "$command = 'scan'\n" + NosqlDynamodbCode),
                        Command("get", "Get one item by key.", "$command = 'get'\n" + NosqlDynamodbCode)),

                Platform("nosql-cassandra", "Inspect Cassandra (read-only)",
                    "Apache Cassandra and ScyllaDB: lists keyspaces and tables, describes a table, or runs a read-only CQL SELECT.",
                    "The user asks to look at Cassandra or ScyllaDB keyspaces, tables, or rows.",
                    "<command> [keyspace | keyspace.table | \"SELECT ...\"]",
                    "Connects to `$CASSANDRA_HOST` (default 127.0.0.1) and `$CASSANDRA_PORT` (default 9042); with `$CASSANDRA_USERNAME` and `$CASSANDRA_PASSWORD` set, the credentials go into a temporary cqlshrc that is deleted afterwards, never onto the command line. `keyspaces` lists keyspaces, `tables <keyspace>` lists its tables, `describe <keyspace.table>` prints the schema, and `query \"SELECT ...\"` runs one SELECT.",
                    null, new[] { "cqlsh" },
                        Command("keyspaces", "List keyspaces.", "$command = 'keyspaces'\n" + NosqlCassandraCode),
                        Command("tables", "List a keyspace's tables.", "$command = 'tables'\n" + NosqlCassandraCode),
                        Command("describe", "Print a table's schema.", "$command = 'describe'\n" + NosqlCassandraCode),
                        Command("query", "Run one SELECT.", "$command = 'query'\n" + NosqlCassandraCode)),

                Platform("graph-neo4j", "Query Neo4j (read-only)",
                    "Neo4j graph database: shows the version, lists node labels and relationship types, or runs a read-only Cypher query.",
                    "The user asks to look at a Neo4j graph: its labels, relationships, or a Cypher MATCH.",
                    "<command> [\"MATCH ... RETURN ...\"]",
                    "cypher-shell reads the connection from `$NEO4J_URI` (or NEO4J_ADDRESS), `$NEO4J_USERNAME`, `$NEO4J_PASSWORD`, and `$NEO4J_DATABASE`, so nothing secret is on the command line. `ping` prints the components and versions, `labels` and `relationships` list node labels and relationship types, and `query \"<cypher>\"` runs one statement. Every session uses `--access-mode read`, so the server rejects writes.",
                    null, new[] { "cypher-shell" },
                        Command("ping", "Print the Neo4j version.", "$command = 'ping'\n" + GraphNeo4jCode),
                        Command("labels", "List node labels.", "$command = 'labels'\n" + GraphNeo4jCode),
                        Command("relationships", "List relationship types.", "$command = 'relationships'\n" + GraphNeo4jCode),
                        Command("query", "Run one read-only Cypher statement.", "$command = 'query'\n" + GraphNeo4jCode)),

                Platform("graph-litegraph", "Query LiteGraph (read-only)",
                    "LiteGraph graph database: checks health, lists tenants and graphs, shows statistics, lists nodes and edges, or runs a read-only graph query (MATCH ... RETURN).",
                    "The user asks to look at a LiteGraph instance: its tenants, graphs, nodes, edges, statistics, or a graph query.",
                    "<command> [\"MATCH ... RETURN ...\"] [--tenant guid] [--graph guid] [--max n]",
                    "Talks to the LiteGraph REST API at `$LITEGRAPH_ENDPOINT` (default http://localhost:8701) with the bearer token in `$LITEGRAPH_API_KEY` (or LITEGRAPH_TOKEN), the same variables the LiteGraph MCP server uses; the token is never shown, and a read-scoped credential is best. The tenant and graph come from `--tenant` and `--graph` or `$LITEGRAPH_TENANT_GUID` and `$LITEGRAPH_GRAPH_GUID`. `ping` calls `/v1.0/health/ready` (no token), `tenants` and `graphs` list them, `stats` shows tenant or graph statistics, `nodes` and `edges` list up to `--max` (default 100), and `query \"MATCH ... RETURN ...\"` runs LiteGraph's native graph query; queries with CREATE, MERGE, SET, DELETE, DETACH, REMOVE, or DROP are refused.",
                    null, null,
                        Command("ping", "Check that the server is ready.", "$command = 'ping'\n" + GraphLitegraphCode),
                        Command("tenants", "List tenants.", "$command = 'tenants'\n" + GraphLitegraphCode),
                        Command("graphs", "List graphs in the tenant.", "$command = 'graphs'\n" + GraphLitegraphCode),
                        Command("stats", "Show tenant or graph statistics.", "$command = 'stats'\n" + GraphLitegraphCode),
                        Command("nodes", "List nodes in the graph.", "$command = 'nodes'\n" + GraphLitegraphCode),
                        Command("edges", "List edges in the graph.", "$command = 'edges'\n" + GraphLitegraphCode),
                        Command("query", "Run one read-only graph query.", "$command = 'query'\n" + GraphLitegraphCode))
            };
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillDef Platform(string id, string title, string description, string whenToUse, string argumentHint, string body, IEnumerable<string>? appliesTo, IEnumerable<string>? requiresTools, params DefaultSkillCommandDef[] commands)
        {
            ToolchainSkillFactory factory = new ToolchainSkillFactory(CommonSetup, new[] { "data", "database" }, appliesTo, requiresTools, PlatformExitNote);
            return factory.Skill(id, title, description, false, whenToUse, argumentHint, body + " Read-only.", commands);
        }

        private static DefaultSkillCommandDef Command(string name, string description, string code)
        {
            return ToolchainSkillFactory.Command(name, description, code);
        }

        #endregion
    }
}
