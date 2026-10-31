FROM python:3.13-slim-trixie@sha256:2d44738ebf4ce5f63b9d9ea871e9c7cfdc0426f7b0c97cac915fd88ded1d86bb
RUN --mount=type=secret,id=proxy-ca,target=/etc/ssl/certs/ca-certificates.crt,mode=0444 \
    sed -i 's|http://deb.debian.org|https://deb.debian.org|g' /etc/apt/sources.list.d/debian.sources \
    && apt-get -o Acquire::Retries=0 -o Acquire::https::Timeout=15 update \
    && apt-get install -y --no-install-recommends network-manager dbus \
    && rm -rf /var/lib/apt/lists/*
ENV IT_N8_REAL_NM_CONTAINER=1 PYTHONDONTWRITEBYTECODE=1
WORKDIR /repo
COPY pi-network/ /repo/pi-network/
COPY pi-satellite/ /repo/pi-satellite/
CMD ["python3", "-I", "/repo/pi-satellite/tests/run_nm_validation.py"]
