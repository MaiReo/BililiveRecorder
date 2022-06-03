FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine
COPY . /src
RUN cd /src/BililiveRecorder.Cli && dotnet restore
RUN cd /src/BililiveRecorder.Cli && dotnet build -o /output -c Release --no-restore

FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine
RUN apk add --no-cache tzdata ca-certificates
ENV TZ=Asia/Shanghai
COPY --from=0 /output /app/BililiveRecorder.Cli
# VOLUME [ "/rec" ]
WORKDIR /app
RUN ln -s ./BililiveRecorder.Cli/BililiveRecorder.Cli brec
ENV BREC_LOG_LEVEL_CONSOLE=Information \
    BREC_RECORD_MODE=Standard \
    BREC_FILENAME_FORMAT="{roomid}/{date}-{time}-{ms}.flv" \
    BREC_DANMAKU_MODE=7 \
    BREC_WEB_BIND="http://*:2356" \
    BREC_WORKDIR=/rec
ENTRYPOINT [ "/app/brec" ]
CMD [ "p" ]
