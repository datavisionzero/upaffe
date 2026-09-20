# Build tools run on the builder's architecture; only the runtime image targets the host.
FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS web
WORKDIR /source/src/web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY docs/api/openapi.json /source/docs/api/openapi.json
COPY src/web/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props Upaffe.slnx ./
COPY src/Upaffe.Domain/Upaffe.Domain.csproj src/Upaffe.Domain/
COPY src/Upaffe.Application/Upaffe.Application.csproj src/Upaffe.Application/
COPY src/Upaffe.Infrastructure/Upaffe.Infrastructure.csproj src/Upaffe.Infrastructure/
COPY src/Upaffe.Api/Upaffe.Api.csproj src/Upaffe.Api/
RUN dotnet restore src/Upaffe.Api/Upaffe.Api.csproj -a $TARGETARCH
COPY src/ ./src/
COPY --from=web /source/src/Upaffe.Api/wwwroot/ src/Upaffe.Api/wwwroot/
ARG APP_VERSION=0.0.0-dev
RUN dotnet publish src/Upaffe.Api/Upaffe.Api.csproj \
    --configuration Release --no-restore --output /app -a $TARGETARCH \
    -p:Version=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache curl krb5-libs
WORKDIR /app
COPY --from=build /app/ ./
ARG VCS_REF=unknown
ARG APP_VERSION=0.0.0-dev
LABEL org.opencontainers.image.source="https://github.com/datavisionzero/upaffe" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.version="${APP_VERSION}"
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["./upaffe"]
