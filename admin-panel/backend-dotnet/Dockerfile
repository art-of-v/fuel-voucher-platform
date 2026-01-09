FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/FuelFlow.Api/FuelFlow.Api.csproj", "src/FuelFlow.Api/"]
RUN dotnet restore "src/FuelFlow.Api/FuelFlow.Api.csproj"
COPY . .
WORKDIR "/src/src/FuelFlow.Api"
RUN dotnet build "FuelFlow.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FuelFlow.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "FuelFlow.Api.dll"]
