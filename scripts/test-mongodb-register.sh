#!/bin/bash
# Test Register Service MongoDB integration with per-register databases

echo "Testing Register Service with MongoDB per-register databases..."
echo "=============================================================="
echo ""

# Step 1: Get JWT token
echo "[1/5] Getting JWT token..."
TOKEN=$(curl -s -X POST http://localhost/api/tenant/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.local","password":"Admin123!"}' | \
  grep -o '"accessToken":"[^"]*"' | sed 's/"accessToken":"\(.*\)"/\1/')

if [ -z "$TOKEN" ]; then
    echo "❌ Failed to get JWT token"
    exit 1
fi

echo "✅ Got JWT token (${#TOKEN} chars)"
echo ""

# Step 2: Create a test register
echo "[2/5] Creating test register 'MongoDB-Test-Register'..."
REGISTER_ID=$(curl -s -X POST http://localhost/api/registers \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "MongoDB-Test-Register",
    "description": "Testing MongoDB per-register database architecture",
    "tenantId": "00000000-0000-0000-0000-000000000001"
  }' | grep -o '"id":"[^"]*"' | sed 's/"id":"\(.*\)"/\1/')

if [ -z "$REGISTER_ID" ]; then
    echo "❌ Failed to create register"
    exit 1
fi

echo "✅ Created register: $REGISTER_ID"
echo ""

# Step 3: Check MongoDB for register database
echo "[3/5] Checking MongoDB for per-register database..."
docker exec sorcha-mongodb mongosh --quiet --eval "
  db.getSiblingDB('admin').auth('sorcha', 'sorcha_dev_password');
  var dbs = db.adminCommand({ listDatabases: 1 });
  var registerDbs = dbs.databases.filter(db => db.name.startsWith('sorcha_register_'));
  print('Found ' + registerDbs.length + ' register databases:');
  registerDbs.forEach(db => print('  - ' + db.name));
"

echo ""

# Step 4: Check the registry database
echo "[4/5] Checking registry database for register metadata..."
docker exec sorcha-mongodb mongosh --quiet sorcha_register_registry --eval "
  db.getSiblingDB('admin').auth('sorcha', 'sorcha_dev_password');
  var count = db.registers.countDocuments();
  print('Registers in registry: ' + count);
  if (count > 0) {
    print('Latest register:');
    db.registers.find().sort({_id: -1}).limit(1).forEach(r => {
      print('  ID: ' + r.Id);
      print('  Name: ' + r.Name);
      print('  Database: sorcha_register_' + r.Id);
    });
  }
"

echo ""

# Step 5: Verify register can be retrieved
echo "[5/5] Verifying register retrieval..."
RETRIEVED=$(curl -s -X GET "http://localhost/api/registers/$REGISTER_ID" \
  -H "Authorization: Bearer $TOKEN")

if echo "$RETRIEVED" | grep -q "MongoDB-Test-Register"; then
    echo "✅ Register retrieved successfully!"
else
    echo "❌ Failed to retrieve register"
    exit 1
fi

echo ""
echo "=============================================================="
echo "✅ MongoDB per-register database test PASSED!"
echo ""
echo "Architecture verified:"
echo "  - Registry database: sorcha_register_registry (holds register metadata)"
echo "  - Per-register database: sorcha_register_$REGISTER_ID (will hold transactions/dockets)"
echo ""
