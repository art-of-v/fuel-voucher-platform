// Stripe Payment Test Script
// Run this after restarting your backend with: node test-payment.js

const API_URL = 'http://localhost:4000';

async function testStripeIntegration() {
    console.log('🧪 Testing Stripe Integration...\n');

    // Step 1: Check Stripe Config
    console.log('1️⃣ Checking Stripe configuration...');
    try {
        const configRes = await fetch(`${API_URL}/api/stripe/config`);
        const config = await configRes.json();

        if (config.publishableKey && config.publishableKey.startsWith('pk_test_')) {
            console.log('✅ Stripe publishable key configured:', config.publishableKey.substring(0, 20) + '...');
        } else {
            console.log('❌ Stripe publishable key not found. Did you restart the backend?');
            return;
        }
    } catch (error) {
        console.log('❌ Failed to connect to backend:', error.message);
        return;
    }

    // Step 2: Create a test purchase
    console.log('\n2️⃣ Creating test purchase...');
    try {
        const purchaseRes = await fetch(`${API_URL}/api/checkout`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                packageId: 'test-pkg-1',
                stationId: 'station-okko-1',
                stationName: 'OKKO',
                fuelType: 'diesel',
                fuelName: 'Diesel Euro',
                liters: 10,  // Changed from 50L to 10L
                price: 500   // 500 UAH for 10L
            })
        });

        const purchase = await purchaseRes.json();

        if (purchase.purchaseId) {
            console.log('✅ Purchase created with ID:', purchase.purchaseId);

            // Step 3: Simulate successful payment
            console.log('\n3️⃣ Simulating successful payment...');
            const paymentRes = await fetch(`${API_URL}/api/payments/simulate`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    purchaseId: purchase.purchaseId,
                    scenario: 'success'
                })
            });

            const result = await paymentRes.json();

            if (result.status === 'success') {
                console.log('✅ Payment successful!');
                console.log('📦 Purchase details:', JSON.stringify(result.purchase, null, 2));
            } else {
                console.log('❌ Payment failed:', result);
            }
        } else {
            console.log('❌ Failed to create purchase:', purchase);
        }
    } catch (error) {
        console.log('❌ Error during purchase flow:', error.message);
    }

    console.log('\n✨ Test complete!');
}

// Run the test
testStripeIntegration().catch(console.error);
