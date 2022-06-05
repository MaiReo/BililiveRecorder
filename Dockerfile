FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine
COPY . /src
RUN cd /src/BililiveRecorder.Cli && dotnet build -o /output -c Release

# FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine
FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine
RUN sed -i 's/dl-cdn.alpinelinux.org/mirrors.ustc.edu.cn/g' /etc/apk/repositories
RUN apk add --no-cache tzdata ca-certificates
ENV TZ=Asia/Shanghai
COPY --from=0 /output /app/BililiveRecorder.Cli
# VOLUME [ "/rec" ]
WORKDIR /app
RUN ln -s ./BililiveRecorder.Cli/BililiveRecorder.Cli brec
ENV BREC_LOG_LEVEL_CONSOLE=Information \
    BREC_RECORD_MODE=Standard \
    BREC_FILENAME_FORMAT='\"now\" | time_zone: \"Asia/Shanghai\" | format_date: \"yyyyMMdd-HHmmss-fff\".flv' \
    BREC_DANMAKU_MODE=7 \
    BREC_WORKDIR=/rec
# ENV BREC_WEB_BIND="http://*:2356"
ENTRYPOINT [ "/app/brec" ]
CMD [ "p" ]
