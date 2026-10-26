# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM debian:trixie-slim AS rootfs-builder
ARG TARGETARCH

ENV DEBIAN_FRONTEND=noninteractive

RUN <<'EOF'
set -eu
if [ "$TARGETARCH" != "$(dpkg --print-architecture)" ]; then
    dpkg --add-architecture "$TARGETARCH"
fi
apt-get update -qq
apt-get install -y -qq --no-install-recommends binutils \
    "libfontconfig1:$TARGETARCH" "libfreetype6:$TARGETARCH" "libsqlite3-0:$TARGETARCH" \
    "libstdc++6:$TARGETARCH" "libacl1:$TARGETARCH" "libattr1:$TARGETARCH" "libcap2:$TARGETARCH" \
    "libcap-ng0:$TARGETARCH" "libgmp10:$TARGETARCH" "libselinux1:$TARGETARCH" \
    "libpcre2-8-0:$TARGETARCH" "libseccomp2:$TARGETARCH"
PACKAGES="coreutils:$TARGETARCH dash:$TARGETARCH util-linux:$TARGETARCH"
if apt-cache show "setpriv:$TARGETARCH" >/dev/null 2>&1; then
    PACKAGES="$PACKAGES setpriv:$TARGETARCH"
fi
mkdir -p /debs /target
cd /debs
apt-get download $PACKAGES
for deb in /debs/*.deb; do dpkg-deb -x "$deb" /target; done
rm -rf /var/lib/apt/lists/* /debs
EOF

RUN groupadd -g 1000 node \
    && useradd -u 1000 -g node -s /usr/sbin/nologin -M -d /home/node node

COPY publish/ /rootfs/app/

# Dependencies are read with readelf rather than ldd: ldd runs the target binary, which would
# need emulation to build arm64 on x86.
RUN <<'EOF'
set -eu
case "$TARGETARCH" in
    arm64) TRIPLET="aarch64-linux-gnu" ;;
    amd64) TRIPLET="x86_64-linux-gnu" ;;
    *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;;
esac
LIBDIR="/rootfs/usr/lib/$TRIPLET"

mkdir -p /rootfs/usr/bin "$LIBDIR" \
         /rootfs/etc/fonts /rootfs/usr/share/fontconfig /rootfs/var/cache/fontconfig \
         /rootfs/data/db /rootfs/data/thumbnails /rootfs/data/backups /rootfs/data/manga_database \
         /rootfs/libraries /rootfs/tmp

for bin in dash setpriv id mkdir chown chmod env cat ls; do
    if [ ! -x "/target/usr/bin/$bin" ]; then
        echo "Missing /usr/bin/$bin in the $TARGETARCH packages" >&2
        exit 1
    fi
    cp "/target/usr/bin/$bin" /rootfs/usr/bin/
done
ln -sf dash /rootfs/usr/bin/sh

find /rootfs/usr/bin /rootfs/app -type f \( -perm -u+x -o -name '*.so' -o -name '*.so.*' \) > /tmp/pending
: > /tmp/seen
missing=""
while [ -s /tmp/pending ]; do
    file="$(head -n 1 /tmp/pending)"
    sed -i 1d /tmp/pending
    readelf -d "$file" 2>/dev/null | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p' > /tmp/needed || true
    while read -r lib; do
        grep -qxF "$lib" /tmp/seen && continue
        echo "$lib" >> /tmp/seen
        [ -e "$LIBDIR/$lib" ] && continue
        [ -n "$(find /rootfs/app -name "$lib*" -print -quit)" ] && continue
        source=""
        for dir in "/usr/lib/$TRIPLET" "/target/usr/lib/$TRIPLET"; do
            if [ -e "$dir/$lib" ]; then source="$dir/$lib"; break; fi
        done
        if [ -z "$source" ]; then
            missing="$missing $lib"
            continue
        fi
        cp -L "$source" "$LIBDIR/$lib"
        echo "$LIBDIR/$lib" >> /tmp/pending
    done < /tmp/needed
done
if [ -n "$missing" ]; then
    echo "Libraries not found for $TARGETARCH:$missing" >&2
    exit 1
fi

cp -L "/usr/lib/$TRIPLET"/libsqlite3.so* "$LIBDIR/"
ln -sf libsqlite3.so.0 "$LIBDIR/libe_sqlite3.so"

cp -a /etc/fonts/. /rootfs/etc/fonts/
cp -a /usr/share/fontconfig/. /rootfs/usr/share/fontconfig/
EOF

COPY --chmod=0755 entrypoint.sh /rootfs/usr/local/bin/entrypoint.sh

RUN chown -R 1000:1000 /rootfs/app /rootfs/data /rootfs/libraries \
    && chmod 1777 /rootfs/tmp

FROM gcr.io/distroless/cc-debian13:nonroot

COPY --from=rootfs-builder /etc/passwd /etc/group /etc/
COPY --from=rootfs-builder /rootfs/ /

LABEL org.opencontainers.image.title="DiarSpeicher" \
    org.opencontainers.image.description="Servidor de comics y libros digitales en .NET 11, servido tras el Gateway"

WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    ConnectionStrings__DefaultConnection="Data Source=/data/db/diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;" \
    Storage__RootPath=/data

VOLUME /data

USER node

ENTRYPOINT ["/bin/sh", "/usr/local/bin/entrypoint.sh"]
CMD ["/app/DiarSpeicher.Api"]
