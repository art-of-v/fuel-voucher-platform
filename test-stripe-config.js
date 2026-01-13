// Simple Stripe Config Test
const API_URL = 'http://localhost:4000';

async function testStripeConfig() {
    console.log('🧪 Testing Stripe Configuration...\n');

    try {
        const response = await fetch(`${API_URL}/api/stripe/config`);
        const config = await response.json();

        console.log('✅ Stripe Integration Status:');
        console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
        console.log('Publishable Key:', config.publishableKey);
        console.log('Key Type:', config.publishableKey.startsWith('pk_test_') ? '🧪 TEST MODE' : '🔴 LIVE MODE');
        console.log('Status:', config.publishableKey ? '✅ CONFIGURED' : '❌ NOT CONFIGURED');
        console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n');

        if (config.publishableKey.startsWith('pk_test_')) {
            console.log('🎉 SUCCESS! Your Stripe test account is ready!');
            console.log('\n📝 Next Steps:');
            console.log('1. Add some vouchers/inventory to your database');
            console.log('2. Open test-payment.html in your browser');
            console.log('3. Test the complete payment flow\n');
        }
    } catch (error) {
        console.log('❌ Error:', error.message);
    }
}

testStripeConfig();
