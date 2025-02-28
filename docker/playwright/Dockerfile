FROM mcr.microsoft.com/dotnet/sdk:6.0

# Install git
RUN apt-get update && apt-get install -y git

# Install playwright dependencies
RUN apt-get install -y libglib2.0-0 libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libdbus-1-3 libxcb1 libxkbcommon0 libx11-6 libxcomposite1 libxdamage1 libxext6 libxfixes3 libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2 libatspi2.0-0 libwayland-client0

# Install playwright
RUN dotnet tool install --global Microsoft.Playwright.CLI
ENV PATH="$PATH:/root/.dotnet/tools"

# Install browsers
RUN playwright install

# Set working directory
WORKDIR /app

# Copy the test running script
COPY run-playwright-tests.sh .
RUN chmod +x run-playwright-tests.sh

# Set the entry point
ENTRYPOINT ["/app/run-playwright-tests.sh"]