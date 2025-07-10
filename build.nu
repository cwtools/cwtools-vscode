def main [profile? : string] {

dotnet tool restore
dotnet paket restore
let exit_code = $env.LAST_EXIT_CODE
if $exit_code != 0 {
    exit $exit_code
}
dotnet run --project build -- -t ($profile | default "QuickBuild")
}
