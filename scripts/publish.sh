set -e

# Needed once
npm install

# Build fat jar
dotnet publish --self-contained -r win-x64 src/Main -o ../../out/server/

# Build vsix
vsce publish patch