#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

dotnet test "$root/tests/Upaffe.IntegrationTests" \
  --filter FullyQualifiedName~ContractTests

npm run typecheck --prefix "$root/src/web"

cd "$root/src/cli"
go generate ./...
go test ./...
