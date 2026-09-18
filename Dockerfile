# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-builder
ARG TARGETARCH
WORKDIR /build

COPY Directory.Packages.props ./
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
    --self-contained true \
    --no-restore \
    -o /build/publish

FROM debian:trixie-slim AS rootfs-builder

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update -qq \
    && apt-get install -y -qq --no-install-recommends \
        libfontconfig1 libfreetype6 libsqlite3-0 \
        coreutils dash util-linux \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -g 1000 node \
    && useradd -u 1000 -g node -s /usr/sbin/nologin -M -d /home/node node

WORKDIR /

RUN TRIPLET="$([ "$(dpkg --print-architecture)" = "arm64" ] && echo "aarch64-linux-gnu" || echo "x86_64-linux-gnu")" \
    && mkdir -p /rootfs/usr/bin "/rootfs/usr/lib/$TRIPLET" \
             /rootfs/etc/fonts \
             /rootfs/usr/share/fontconfig \
             /rootfs/var/cache/fontconfig \
             /rootfs/data/db /rootfs/data/thumbnails \
             /rootfs/data/backups /rootfs/data/manga_database \
             /rootfs/libraries /rootfs/app /rootfs/tmp

RUN cp /usr/bin/dash           /rootfs/usr/bin/ \
    && cp /usr/bin/setpriv     /rootfs/usr/bin/ \
    && cp /usr/bin/id          /rootfs/usr/bin/ \
    && cp /usr/bin/mkdir /usr/bin/chown /usr/bin/chmod /usr/bin/env \
          /usr/bin/cat /usr/bin/ls \
          /rootfs/usr/bin/ \
    && ln -sf dash /rootfs/usr/bin/sh

COPY --from=dotnet-builder /build/publish/ /rootfs/app/

# El rootfs se arma resolviendo las dependencias reales con ldd en lugar de listarlas a mano:
# libSkiaSharp.so arrastra fontconfig, freetype, expat, png y zlib de forma transitiva, y esa
# cadena cambia entre versiones de SkiaSharp. Listarla fija seria falsear la dependencia.
RUN set -eu; \
    TRIPLET="$([ "$(dpkg --print-architecture)" = "arm64" ] && echo "aarch64-linux-gnu" || echo "x86_64-linux-gnu")"; \
    { for bin in /rootfs/usr/bin/dash /rootfs/usr/bin/setpriv /rootfs/usr/bin/id \
                 /rootfs/usr/bin/mkdir /rootfs/usr/bin/chown /rootfs/usr/bin/chmod \
                 /rootfs/usr/bin/env /rootfs/usr/bin/cat /rootfs/usr/bin/ls; do \
          ldd "$bin" 2>/dev/null | awk '/=> \//{print $3}'; \
      done; \
      find /rootfs/app -name '*.so' -o -name '*.so.*' | while read -r so; do \
          ldd "$so" 2>/dev/null | awk '/=> \//{print $3}'; \
      done; \
    } | sort -u | while read -r lib; do \
        case "$lib" in /rootfs/*) continue ;; esac; \
        cp -L "$lib" "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    done; \
    cp -L /lib*/ld-linux-*.so* "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    cp -L "/usr/lib/$TRIPLET"/libsqlite3.so* "/rootfs/usr/lib/$TRIPLET/" 2>/dev/null || true; \
    ln -sf libsqlite3.so.0 "/rootfs/usr/lib/$TRIPLET/libe_sqlite3.so"

RUN cp -a /etc/fonts/. /rootfs/etc/fonts/ \
    && cp -a /usr/share/fontconfig/. /rootfs/usr/share/fontconfig/

COPY --chmod=0755 entrypoint.sh /rootfs/usr/local/bin/entrypoint.sh

RUN chown -R 1000:1000 /rootfs/app /rootfs/data /rootfs/libraries \
    && chmod 1777 /rootfs/tmp

FROM gcr.io/distroless/cc-debian13:nonroot

COPY --from=rootfs-builder /etc/passwd /etc/group /etc/
COPY --from=rootfs-builder /rootfs/ /

LABEL org.opencontainers.image.title="DiarSpeicher" \
    org.opencontainers.image.description="Servidor de comics y libros digitales en .NET 10, servido tras el Gateway"

WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    ConnectionStrings__DefaultConnection="Data Source=/data/db/diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;" \
    Storage__RootPath=/data

VOLUME /data

USER node

ENTRYPOINT ["/bin/sh", "/usr/local/bin/entrypoint.sh"]
CMD ["/app/DiarSpeicher.Api"]
