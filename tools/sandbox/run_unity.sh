#!/bin/sh
export LD_LIBRARY_PATH=/work/unity/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export HOME=/work/unity/home
export JAVA_HOME=/work/unity/jdk11
export ANDROID_SDK_ROOT=/work/unity/android-sdk
export ANDROID_NDK_ROOT=/work/unity/android-sdk/ndk/23.1.7779620
export LD_PRELOAD=/work/unity/shim/libschedfix.so${LD_PRELOAD:+:$LD_PRELOAD}
exec /work/unity/editor/Editor/Unity "$@"
