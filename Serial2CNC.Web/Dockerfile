FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Serial2CNC.Web/Serial2CNC.Web.csproj", "Serial2CNC.Web/"]
COPY ["Serial2CNC.Core/Serial2CNC.Core.csproj", "Serial2CNC.Core/"]
COPY ["Serial2CNC.Security/Serial2CNC.Security.csproj", "Serial2CNC.Security/"]
RUN dotnet restore "Serial2CNC.Web/Serial2CNC.Web.csproj"

COPY . .
RUN dotnet publish "Serial2CNC.Web/Serial2CNC.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Serial2CNC.Web.dll"]
