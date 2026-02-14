#!/bin/bash

echo "🚀 Testing InsightEngine API - JSON Metadata Storage"
echo ""

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

API_BASE="http://localhost:5000/api/v1"
UPLOAD_FILE="samples/ecommerce_sales.csv"

echo -e "${BLUE}1. Testing Upload Endpoint${NC}"
echo "POST $API_BASE/datasets"
echo ""

UPLOAD_RESPONSE=$(curl -s -X POST "$API_BASE/datasets" \
  -F "file=@$UPLOAD_FILE" \
  -w "\n%{http_code}")

HTTP_CODE=$(echo "$UPLOAD_RESPONSE" | tail -n1)
RESPONSE_BODY=$(echo "$UPLOAD_RESPONSE" | head -n-1)

if [ "$HTTP_CODE" = "201" ]; then
    echo -e "${GREEN}✅ Upload successful (201 Created)${NC}"
    echo "$RESPONSE_BODY" | jq '.'
    
    # Extract datasetId from response
    DATASET_ID=$(echo "$RESPONSE_BODY" | jq -r '.data.datasetId')
    echo ""
    echo -e "${BLUE}Dataset ID: $DATASET_ID${NC}"
    echo ""
else
    echo -e "${RED}❌ Upload failed (HTTP $HTTP_CODE)${NC}"
    echo "$RESPONSE_BODY"
    exit 1
fi

echo ""
echo "=================================="
echo ""

echo -e "${BLUE}2. Testing Get All Datasets${NC}"
echo "GET $API_BASE/datasets"
echo ""

ALL_RESPONSE=$(curl -s "$API_BASE/datasets")
echo "$ALL_RESPONSE" | jq '.'

echo ""
echo "=================================="
echo ""

echo -e "${BLUE}3. Testing Get Dataset by ID${NC}"
echo "GET $API_BASE/datasets/$DATASET_ID"
echo ""

GET_RESPONSE=$(curl -s "$API_BASE/datasets/$DATASET_ID")
echo "$GET_RESPONSE" | jq '.'

echo ""
echo "=================================="
echo ""

echo -e "${BLUE}4. Testing Get Dataset Profile${NC}"
echo "GET $API_BASE/datasets/$DATASET_ID/profile"
echo ""

PROFILE_RESPONSE=$(curl -s "$API_BASE/datasets/$DATASET_ID/profile")
echo "$PROFILE_RESPONSE" | jq '.'

echo ""
echo "=================================="
echo ""

echo -e "${BLUE}5. Checking File System${NC}"
echo "Looking for files in uploads/ directory:"
echo ""

if [ -d "src/InsightEngine.API/uploads" ]; then
    ls -lah "src/InsightEngine.API/uploads/"
    echo ""
    
    if [ -f "src/InsightEngine.API/uploads/$DATASET_ID.csv" ]; then
        echo -e "${GREEN}✅ CSV file exists: $DATASET_ID.csv${NC}"
    else
        echo -e "${RED}❌ CSV file not found${NC}"
    fi
    
    if [ -f "src/InsightEngine.API/uploads/$DATASET_ID.meta.json" ]; then
        echo -e "${GREEN}✅ Metadata file exists: $DATASET_ID.meta.json${NC}"
        echo ""
        echo "Metadata content:"
        cat "src/InsightEngine.API/uploads/$DATASET_ID.meta.json" | jq '.'
    else
        echo -e "${RED}❌ Metadata file not found${NC}"
    fi
else
    echo -e "${RED}❌ uploads/ directory not found${NC}"
fi

echo ""
echo -e "${GREEN}🎉 All tests completed!${NC}"
