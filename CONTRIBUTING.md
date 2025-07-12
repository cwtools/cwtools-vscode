# Contributing

To use a local cwtools git repo, create a file called `cwtools.local.props` containing:

```xml
<Project>
  <PropertyGroup>
    <!-- turn on the local path and define where cwtools lives -->
    <UseLocalCwtools Condition="'$(UseLocalCwtools)' == ''">>True</UseLocalCwtools>
    <CwtoolsPath>../../../cwtools/cwtools/cwtools.fsproj</CwtoolsPath>
  </PropertyGroup>
</Project>
```

And amend the path to your repo. The default assumes it's adjacent to this repo.