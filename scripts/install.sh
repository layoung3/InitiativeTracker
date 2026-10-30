#!/bin/bash -p
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
exec /usr/bin/python3 -I "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")/installation/install.py" "$@"
