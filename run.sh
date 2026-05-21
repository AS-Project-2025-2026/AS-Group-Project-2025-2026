#!/usr/bin/env bash
set -e

echo "Building and starting all services..."
docker compose up --build -d

echo ""
echo "Services:"
echo "  nopCommerce  -> http://localhost:8080"
echo "  RabbitMQ UI  -> http://localhost:15672  (guest / guest)"
echo "  Warehouse    -> http://localhost:5081/health"
echo "  Shipping     -> http://localhost:5082/health"
echo ""
echo "To follow logs: docker compose logs -f"
echo "To stop:        docker compose down"
