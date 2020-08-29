FROM mcr.microsoft.com/dotnet/core/aspnet:5.0-alpine

RUN echo 'http://dl-cdn.alpinelinux.org/alpine/v3.8/main' >> /etc/apk/repositories && \
    apk update --no-cache && \
    apk add --no-cache bash libc6-compat=1.1.19-r11

ADD ./bin/Release/netcoreapp5/linux-musl-x64/publish/ /app

WORKDIR /app
EXPOSE 12001
ENTRYPOINT [ "dotnet", "Worker.dll" ]