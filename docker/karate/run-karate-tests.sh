#!/bin/bash
set -e

# Clone the repository
git clone $REPO_URL .

# Run the Karate tests
mvn test