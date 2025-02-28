FROM maven:3.8-openjdk-11

# Install git
RUN apt-get update && apt-get install -y git

# Set working directory
WORKDIR /app

# Copy the test running script
COPY run-karate-tests.sh .
RUN chmod +x run-karate-tests.sh

# Set the entry point
ENTRYPOINT ["/app/run-karate-tests.sh"]