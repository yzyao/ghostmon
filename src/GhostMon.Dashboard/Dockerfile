FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

RUN apk add --no-cache clang lld musl-dev zlib-dev

COPY src/GhostMon.Contracts/GhostMon.Contracts.csproj src/GhostMon.Contracts/
COPY src/GhostMon.Dashboard/GhostMon.Dashboard.csproj src/GhostMon.Dashboard/

RUN dotnet restore src/GhostMon.Dashboard/GhostMon.Dashboard.csproj

COPY . .

RUN dotnet publish src/GhostMon.Dashboard/GhostMon.Dashboard.csproj \
    -c Release \
    -r linux-musl-x64 \
    --self-contained true \
    -p:PublishAot=true \
    -o /app/publish

FROM alpine:3.20 AS final
RUN apk add --no-cache libstdc++ libgcc zlib ca-certificates
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["./GhostMon.Dashboard"]
