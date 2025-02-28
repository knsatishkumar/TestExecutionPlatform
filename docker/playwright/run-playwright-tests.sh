#!/bin/bash
set -e

# Clone the repository
git clone $REPO_URL .

# Restore dependencies
dotnet restore

# Run the playwright tests
dotnet test