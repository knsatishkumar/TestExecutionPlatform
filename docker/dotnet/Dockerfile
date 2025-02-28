FROM mcr.microsoft.com/dotnet/sdk:6.0

# Install git
RUN apt-get update && apt-get install -y git

# Set working directory
WORKDIR /app

# Copy the test running script
COPY run-tests.sh .
RUN chmod +x run-dotnet-tests.sh

# Set the entry point
ENTRYPOINT ["/app/run-tests.sh"]