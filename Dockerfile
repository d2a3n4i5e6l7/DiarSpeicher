# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-builder
ARG TARGETARCH
WORKDIR /build

COPY src/DiarSpeicher.Core/DiarSpeicher.Core.csproj src/DiarSpeicher.Core/
COPY src/DiarSpeicher.Infrastructure/DiarSpeicher.Infrastructure.csproj src/DiarSpeicher.Infrastructure/
COPY src/DiarSpeicher.Api/DiarSpeicher.Api.csproj src/DiarSpeicher.Api/
RUN RID="linux-x64"; \
    if [ "$TARGETARCH" = "arm64" ]; then RID="linux-arm64"; fi; \
    dotnet restore src/DiarSpeicher.Api/DiarSpeicher.Api.csproj -r "$RID"

COPY src/ src/
COPY frontEnd/public/fonts/ frontEnd/public/fonts/

RUN RID="linux-x64"; \
    if [ "$TARGETARCH" = "arm64" ]; then RID="linux-arm64"; fi; \
    dotnet publish src/DiarSpeicher.Api/DiarSpeicher.Api.csproj \
    -c Release \
    -r "$RID" \
    --self-contained false \
    --no-restore \
    -o /build/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

ENV DEBIAN_FRONTEND=noninteractive

RUN set -eu; \
    apt-get update -qq; \
    apt-get install -y -qq --no-install-recommends libfontconfig1 libfreetype6; \
    rm -rf /var/lib/apt/lists/*; \
    taken_user="$(getent passwd 1000 | cut -d: -f1)"; \
    [ -z "$taken_user" ] || userdel -r "$taken_user" 2>/dev/null || userdel "$taken_user" || true; \
    taken_group="$(getent group 1000 | cut -d: -f1)"; \
    [ -z "$taken_group" ] || groupdel "$taken_group" || true; \
    groupadd -g 1000 node; \
    useradd -u 1000 -g node -s /usr/sbin/nologin -M -d /home/node node

WORKDIR /app

COPY --chown=1000:1000 --from=dotnet-builder /build/publish/ /app/
COPY --chmod=0755 entrypoint.sh /usr/local/bin/entrypoint.sh
RUN mkdir -p /data/db /data/thumbnails /data/cache/pages /data/manga_database /libraries \
    && chown -R 1000:1000 /data \
    && chmod 1777 /tmp

LABEL org.opencontainers.image.title="DiarSpeicher" \
    org.opencontainers.image.description="Servidor de comics y libros digitales en .NET 10, servido tras el Gateway"

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    ConnectionStrings__DefaultConnection="Data Source=/data/db/diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;" \
    Storage__RootPath=/data

VOLUME /data

USER node

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "/app/DiarSpeicher.Api.dll"]
