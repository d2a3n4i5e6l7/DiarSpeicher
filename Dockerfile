# syntax=docker/dockerfile:1

# Stage 1: publicar el backend .NET 10 como binario autocontenido
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
RUN RID="linux-x64"; \
    if [ "$TARGETARCH" = "arm64" ]; then RID="linux-arm64"; fi; \
    dotnet publish src/DiarSpeicher.Api/DiarSpeicher.Api.csproj \
    -c Release \
    -r "$RID" \
    --self-contained \
    --no-restore \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o /build/publish

# Stage 2: armar el rootfs sobre debian
FROM debian:trixie-slim AS rootfs-builder

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update -qq \
    && apt-get install -y -qq --no-install-recommends \
        libfontconfig1 libfreetype6 libicu76 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=dotnet-builder /build/publish/DiarSpeicher.Api /rootfs/usr/bin/diarspeicher

RUN set -eu; \
    TRIPLET="$([ "$(dpkg --print-architecture)" = "arm64" ] && echo "aarch64-linux-gnu" || echo "x86_64-linux-gnu")"; \
    mkdir -p "/rootfs/usr/lib/$TRIPLET" /rootfs/etc/fonts \
             /rootfs/data/db /rootfs/data/storage /rootfs/libraries; \
    chmod +x /rootfs/usr/bin/diarspeicher; \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/probe timeout 5 /rootfs/usr/bin/diarspeicher >/dev/null 2>&1 || true; \
    { find /tmp/probe -name '*.so' 2>/dev/null; echo /rootfs/usr/bin/diarspeicher; } \
    | while read -r so; do ldd "$so" 2>/dev/null | awk '/=> \//{print $3}'; done \
    | sort -u | while read -r lib; do \
        cp -L "$lib" "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    done; \
    cp -L "/usr/lib/$TRIPLET"/libicu*.so.* "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    cp -a /etc/fonts/. /rootfs/etc/fonts/; \
    cp -L /lib*/ld-linux-*.so* "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    rm -rf /tmp/probe

RUN chown -R 65532:65532 /rootfs/data

# Stage 3: runtime distroless
FROM gcr.io/distroless/base-debian13:nonroot

COPY --from=rootfs-builder /rootfs/ /

LABEL org.opencontainers.image.title="DiarSpeicher" \
      org.opencontainers.image.description="Servidor de comics y libros digitales en .NET 10, servido tras el Gateway"

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    ConnectionStrings__DefaultConnection="Data Source=/data/db/diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;" \
    Storage__RootPath=/data/storage \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net

VOLUME /data

EXPOSE 5000

USER nonroot

ENTRYPOINT ["/usr/bin/diarspeicher"]
