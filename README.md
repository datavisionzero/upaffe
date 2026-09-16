# upaffe

upaffe is a self-hosted monitoring tool for people who operate software and
infrastructure with AI agents. It is under active development and has not been
released.

The repository currently contains the technical walking skeleton. It does not
yet implement monitoring; [`VISION.md`](./VISION.md) defines the committed MVP.

## Backend development

The backend requires the .NET SDK selected by `global.json`.

```sh
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project src/Upaffe.Api
```

While the host is running, `GET /api/health/live` checks only that the process
can answer. It is a technical liveness signal, not evidence that monitoring is
working.
