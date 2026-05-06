FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY TestingSimulator.csproj ./
RUN dotnet restore TestingSimulator.csproj

COPY . ./
RUN dotnet publish TestingSimulator.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libcap2-bin \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data

COPY --from=build /app/publish ./
COPY pq-curves.json /app/pq-curves.json

RUN setcap 'cap_net_bind_service=+ep' /usr/share/dotnet/dotnet \
    && chmod -R a+rX /app \
    && chmod 0777 /data

WORKDIR /data

EXPOSE 502/tcp
VOLUME ["/data"]

CMD ["sh", "-c", "cp -n /app/pq-curves.json /data/pq-curves.json && exec dotnet /app/TestingSimulator.dll"]
