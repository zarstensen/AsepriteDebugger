# syntax=docker/dockerfile:1

ARG ASEPRITE_VERSION="v1.3-rc6"
ARG ASEPRITE_SKIA_VERSION="m102-861e4743af"
ARG ASEPRITE_USER_FOLDER="/root/.config/aseprite/"
ARG ASEPRITE_EXT_FOLDER="!AsepriteDebugger"

# 7.0-bookworm-slim is on debian 12, which is also where aseprite is compiled.
# if these verisons do not match, the container might exit with an segmentation fault.
FROM mcr.microsoft.com/dotnet/sdk:7.0-bookworm-slim AS dotnet-sdk

################################################################################
# stage for setting up and building test proj.
FROM dotnet-sdk AS test-build

    COPY /src/ /src/

    WORKDIR /src/Tests/TestRunner/

    RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
        dotnet build
    
################################################################################
# stage for compiling aseprite in the required configuration.
FROM debian:12-slim as aseprite-build

    # install requirements for building aseprite through apt-get

    RUN --mount=target=/var/lib/apt/lists,type=cache,sharing=locked \
        --mount=target=/var/cache/apt,type=cache,sharing=locked \
        rm -f /etc/apt/apt.conf.d/docker-clean \
        && apt-get update\
        && apt-get install -y git unzip curl \
        # Aseprite build dependencies
        g++ clang libc++-dev libc++abi-dev cmake ninja-build libx11-dev libxcursor-dev libxi-dev libgl1-mesa-dev libfontconfig1-dev

    # build aseprite executable
    # 
    # aseprite is built with UI support, even though the docker container is headless,
    # this is due to aseprite only working properly with websockets if the UI is on,
    # as the separate thread for sending and recieving messages otherwise is not run.
    #
    # To run aseprite with UI we also need xvfb later on, to simulate a display for aseprite, to avoid a segmentation fault.

    ARG ASEPRITE_VERSION

    RUN git clone --jobs 24 --recursive --branch ${ASEPRITE_VERSION} https://github.com/aseprite/aseprite usr/src/aseprite

    # install special skia dependency.
    ARG ASEPRITE_SKIA_VERSION
    RUN curl -L -o skia.zip https://github.com/aseprite/skia/releases/download/${ASEPRITE_SKIA_VERSION}/Skia-Linux-Release-x64-libc++.zip \
        && unzip skia.zip -d skia

    ENV CC=clang
    ENV CXX=clang++

    RUN cmake \
        -G Ninja \
        -Wno-dev \
        -DCMAKE_BUILD_TYPE=Debug \
        -DCMAKE_CXX_FLAGS:STRING=-stdlib=libc++ \
        -DCMAKE_EXE_LINKER_FLAGS:STRING=-stdlib=libc++ \
        -DLAF_BACKEND=skia \
        -DSKIA_DIR=/skia \
        -DSKIA_LIBRARY_DIR=skia/out/Release-x64 \
        -DENABLE_UI=ON \
        -DENABLE_NEWS=OFF \
        -DENABLE_UPDATER=OFF \
        -S usr/src/aseprite -B usr/src/aseprite/build

    RUN cmake --build usr/src/aseprite/build --config Debug -j 24
    RUN chmod +xrw usr/src/aseprite/build/bin/aseprite

    # runtime dependencies of aseprite are copied over to /aseprite/dependencies, and should be copied directly to lib on the test image.

    RUN mkdir -p /aseprite/dependencies
    RUN ldd usr/src/aseprite/build/bin/aseprite | awk '{if (match($3, /^\//)) print $3}' | xargs -I '{}' cp --parents '{}' /aseprite/dependencies

################################################################################
# stage for generating aseprite ini file with test script permissions.
FROM dotnet-sdk AS test-prepare

    # when running in UI mode, the user is prompted whenever a script does certain actions.
    # this includes connecting to a server using websockets.
    # However, we do not have access to this UI, so to prevent this prompt from opening, we instead modify the aseprite.ini file,
    # to give all of our test scripts full script permissions.
    # How exactly this is done, can be seen in the PrepareAseprite c# script.

    COPY /src/Tests/PrepareAseprite/ /src/Tests/PrepareAseprite/

    ARG ASEPRITE_USER_FOLDER
    ARG ASEPRITE_EXT_FOLDER

    COPY /src/Debugger/ ${ASEPRITE_USER_FOLDER}/extensions/${ASEPRITE_EXT_FOLDER}

    WORKDIR /src/Tests/PrepareAseprite/

    # environment variables are required for the script.
    ENV ASEPRITE_USER_FOLDER ${ASEPRITE_USER_FOLDER}

    RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
        dotnet run

    # an aseprite.ini which contains permissions for all scripts in ASEDEB_TEST_SCRIPT_DIR, 
    # has now been written to ASEPRITE_USER_FOLDER

################################################################################
# stage for actually running the tests
FROM dotnet-sdk AS tests

    # dependencies for running aseprite with UI

    RUN apt-get update && \
        apt-get install -y xvfb

    COPY --from=aseprite-build usr/src/aseprite/build/bin/ /bin/
    COPY --from=aseprite-build aseprite/dependencies/lib/ /lib/

    ARG ASEPRITE_USER_FOLDER

    COPY --from=test-prepare ${ASEPRITE_USER_FOLDER} ${ASEPRITE_USER_FOLDER}
    COPY --from=test-build /src/ /src/

    WORKDIR /src/Tests/TestRunner/

    ENV ASEPRITE_USER_FOLDER ${ASEPRITE_USER_FOLDER}
    ENV ASEDEB_TEST_XVFB true

    CMD [ "dotnet", "test", "--no-build" ]
