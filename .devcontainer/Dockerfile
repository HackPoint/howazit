FROM mcr.microsoft.com/dotnet/sdk:9.0

# Optional: tooling for dev/test
RUN apt-get update && apt-get install -y curl jq sqlite3 git && rm -rf /var/lib/apt/lists/*

# Node is not required, but handy for tooling; comment out if not needed
# RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && apt-get install -y nodejs

WORKDIR /workspaces