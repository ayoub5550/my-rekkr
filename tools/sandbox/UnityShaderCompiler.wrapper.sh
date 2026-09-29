#!/bin/sh
# gVisor sandbox: the native compiler crashes in PESetupFS (arch_prctl FS/GS not honoured). Run it under qemu user-mode (TCG).
exec /work/qemu/sysroot/usr/bin/qemu-x86_64-static "$(dirname "$0")/UnityShaderCompiler.real" "$@"
