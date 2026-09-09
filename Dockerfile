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
    libfontconfig1 libfreetype6 libicu76 util-linux dash coreutils \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -g 1000 node \
    && useradd -u 1000 -g node -s /usr/sbin/nologin -M -d /home/node node

COPY --from=dotnet-builder /build/publish/DiarSpeicher.Api /rootfs/usr/bin/diarspeicher
COPY entrypoint.sh /rootfs/usr/local/bin/entrypoint.sh

RUN set -eu; \
    TRIPLET="$([ "$(dpkg --print-architecture)" = "arm64" ] && echo "aarch64-linux-gnu" || echo "x86_64-linux-gnu")"; \
    mkdir -p "/rootfs/usr/lib/$TRIPLET" /rootfs/etc/fonts \
    /rootfs/data/db /rootfs/data/thumbnails /rootfs/data/cache/pages \
    /rootfs/libraries /rootfs/usr/bin /rootfs/usr/local/bin /rootfs/tmp; \
    chmod +x /rootfs/usr/bin/diarspeicher /rootfs/usr/local/bin/entrypoint.sh; \
    cp -L /bin/dash /rootfs/usr/bin/dash; \
    ln -sf dash /rootfs/usr/bin/sh; \
    cp -L /bin/mkdir /rootfs/usr/bin/mkdir; \
    cp -L /bin/chown /rootfs/usr/bin/chown; \
    cp -L /bin/chmod /rootfs/usr/bin/chmod; \
    cp -L /usr/bin/id /rootfs/usr/bin/id; \
    cp -L /usr/bin/setpriv /rootfs/usr/bin/setpriv; \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/probe timeout 5 /rootfs/usr/bin/diarspeicher >/dev/null 2>&1 || true; \
    { find /tmp/probe -name '*.so' 2>/dev/null; \
    echo /rootfs/usr/bin/diarspeicher \
    /rootfs/usr/bin/dash \
    /rootfs/usr/bin/mkdir \
    /rootfs/usr/bin/chown \
    /rootfs/usr/bin/chmod \
    /rootfs/usr/bin/id \
    /rootfs/usr/bin/setpriv; \
    } \
    | while read -r so; do ldd "$so" 2>/dev/null | awk '/=> \//{print $3}'; done \
    | sort -u | while read -r lib; do \
    cp -L "$lib" "/rootfs/usr/lib/$TRIPLET/"; \
    done; \
    cp -L "/usr/lib/$TRIPLET"/libicu*.so.* "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    cp -L "/usr/lib/$TRIPLET"/libselinux.so.1 "/rootfs/usr/lib/$TRIPLET/"; \
    cp -L "/usr/lib/$TRIPLET"/libpcre2-8.so.0 "/rootfs/usr/lib/$TRIPLET/"; \
    cp -L "/usr/lib/$TRIPLET"/libcap-ng.so.0 "/rootfs/usr/lib/$TRIPLET/"; \
    cp -a /etc/fonts/. /rootfs/etc/fonts/; \
    cp -L /lib*/ld-linux-*.so* "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    rm -rf /tmp/probe

# El rootfs se arma copiando libs a mano: sin esta verificacion, una dependencia
# ausente no aparece hasta que el contenedor entra en bucle de reinicio.
RUN set -eu; \
    TRIPLET="$([ "$(dpkg --print-architecture)" = "arm64" ] && echo "aarch64-linux-gnu" || echo "x86_64-linux-gnu")"; \
    MISSING=0; \
    for bin in /rootfs/usr/bin/dash /rootfs/usr/bin/mkdir /rootfs/usr/bin/chown \
               /rootfs/usr/bin/chmod /rootfs/usr/bin/id /rootfs/usr/bin/setpriv; do \
        for lib in $(ldd "$bin" 2>/dev/null | awk '/=> \//{print $3}'); do \
            [ -f "/rootfs/usr/lib/$TRIPLET/$(basename "$lib")" ] || { \
                echo "FALTA $(basename "$lib") (requerida por $bin)" >&2; MISSING=1; }; \
        done; \
    done; \
    [ "$MISSING" = 0 ]

RUN chown -R 1000:1000 /rootfs/data

# Stage 3: runtime distroless
FROM gcr.io/distroless/base-debian13:nonroot

COPY --from=rootfs-builder /etc/passwd /etc/group /etc/
COPY --from=rootfs-builder /rootfs/ /

LABEL org.opencontainers.image.title="DiarSpeicher" \
    org.opencontainers.image.description="Servidor de comics y libros digitales en .NET 10, servido tras el Gateway"

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    ConnectionStrings__DefaultConnection="Data Source=/data/db/diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;" \
    Storage__RootPath=/data \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net

VOLUME /data

USER node

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["/usr/bin/diarspeicher"]
