# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy chỉ file .csproj trước để tận dụng cache layer của Docker cho bước restore
COPY ["src/SplitBill.Api/SplitBill.Api.csproj", "src/SplitBill.Api/"]
COPY ["src/SplitBill.Application/SplitBill.Application.csproj", "src/SplitBill.Application/"]
COPY ["src/SplitBill.Domain/SplitBill.Domain.csproj", "src/SplitBill.Domain/"]
COPY ["src/SplitBill.Infrastructure/SplitBill.Infrastructure.csproj", "src/SplitBill.Infrastructure/"]
RUN dotnet restore "src/SplitBill.Api/SplitBill.Api.csproj"

COPY src/SplitBill.Api/ src/SplitBill.Api/
COPY src/SplitBill.Application/ src/SplitBill.Application/
COPY src/SplitBill.Domain/ src/SplitBill.Domain/
COPY src/SplitBill.Infrastructure/ src/SplitBill.Infrastructure/

WORKDIR /src/src/SplitBill.Api
RUN dotnet publish "SplitBill.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SplitBill.Api.dll"]
