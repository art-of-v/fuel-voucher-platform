#!/bin/bash
# Verification script to confirm button affordance fixes are present

echo "🔍 Verifying button affordance fixes..."
echo ""

# Check 1: basket.tsx should have variant="primary"
echo "1️⃣ Checking basket.tsx for primary variant..."
if grep -q 'variant="primary"' app/basket.tsx; then
    echo "   ✅ basket.tsx: Button uses variant=\"primary\""
else
    echo "   ❌ basket.tsx: Button still using wrong variant"
    exit 1
fi

# Check 2: Button.tsx should have 2px border for primary
echo "2️⃣ Checking Button.tsx for 2px border fix..."
if grep -A 3 "case 'primary':" src/core/ui/Button.tsx | grep -q "borderWidth: 2"; then
    echo "   ✅ Button.tsx: Primary variant has 2px border"
else
    echo "   ❌ Button.tsx: Missing 2px border fix"
    exit 1
fi

# Check 3: Verify we're on the right commit
echo "3️⃣ Checking git commit..."
CURRENT_COMMIT=$(git rev-parse HEAD)
echo "   Current commit: $CURRENT_COMMIT"

# Check 4: Verify the comment explaining the fix is present
if grep -q "known rendering issue with solid" src/core/ui/Button.tsx; then
    echo "   ✅ Button.tsx: Fix comment present"
else
    echo "   ❌ Button.tsx: Fix comment missing"
    exit 1
fi

echo ""
echo "✅ All button affordance fixes are present in the code!"
echo ""
echo "Next steps on Mac Mini:"
echo "  1. git pull origin main"
echo "  2. cd mobile && rm -rf node_modules .expo ios/build"
echo "  3. npm install"
echo "  4. npx expo prebuild --clean"
echo "  5. Build in Xcode with clean build folder"
