#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

cd "$root"
dotnet restore Upaffe.slnx
dotnet build Upaffe.slnx --configuration Release --no-restore
dotnet test tests/Upaffe.UnitTests --configuration Release --no-build --no-restore
dotnet test tests/Upaffe.IntegrationTests --configuration Release --no-build --no-restore

npm ci --prefix src/web
npm run typecheck --prefix src/web
npm run lint --prefix src/web
npm test --prefix src/web
npm run build --prefix src/web -- --outDir dist --emptyOutDir

cd "$root/src/cli"
go generate ./...
go vet ./...
go test ./...
go build ./...

cd "$root"
docker compose -f deploy/docker-compose.dev.yml config --quiet
