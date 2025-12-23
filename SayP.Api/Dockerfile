FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["SayP.Api/SayP.Api.csproj", "SayP.Api/"]
COPY ["SayP.Application/SayP.Application.csproj", "SayP.Application/"]
COPY ["SayP.Domain/SayP.Domain.csproj", "SayP.Domain/"]
COPY ["SayP.Infrastructure/SayP.Infrastructure.csproj", "SayP.Infrastructure/"]
RUN dotnet restore "SayP.Api/SayP.Api.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/SayP.Api"
RUN dotnet build "SayP.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SayP.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy .env file
COPY ../.env .env

# Install EF Core tools for migrations
RUN dotnet tool install --global dotnet-ef
ENV PATH="${PATH}:/root/.dotnet/tools"

ENTRYPOINT ["dotnet", "SayP.Api.dll"]
