# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/SplitBill.Web/SplitBill.Web.csproj", "src/SplitBill.Web/"]
COPY ["src/SplitBill.Application/SplitBill.Application.csproj", "src/SplitBill.Application/"]
COPY ["src/SplitBill.Domain/SplitBill.Domain.csproj", "src/SplitBill.Domain/"]
RUN dotnet restore "src/SplitBill.Web/SplitBill.Web.csproj"

COPY src/SplitBill.Web/ src/SplitBill.Web/
COPY src/SplitBill.Application/ src/SplitBill.Application/
COPY src/SplitBill.Domain/ src/SplitBill.Domain/

WORKDIR /src/src/SplitBill.Web
RUN dotnet publish "SplitBill.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SplitBill.Web.dll"]
