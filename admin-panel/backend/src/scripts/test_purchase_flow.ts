
import fetch from "node-fetch";

const API_URL = "http://localhost:4000/api";

async function main() {
    console.log("Testing Purchase Flow against Container...");

    // Test 1: Diesel
    console.log("\n--- TEST 1: Diesel 20L ---");
    await runTest("Diesel", 20);

    // Test 2: A-95
    console.log("\n--- TEST 2: A-95 ---");
    await runTest("A-95", 10);
}

async function runTest(fuelName: string, liters: number) {
    // 1. Checkout
    console.log(`Step 1: Checkout (${fuelName} ${liters}L)...`);
    const checkoutPayload = {
        packageId: `test-pkg-${fuelName}-${liters}`,
        stationId: "okko",
        stationName: "OKKO",
        fuelType: `okko-${fuelName.toLowerCase()}`, // Just dummy ID
        fuelName: fuelName,
        liters: liters,
        price: 500
    };

    const checkoutRes = await fetch(`${API_URL}/checkout`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(checkoutPayload)
    });

    if (!checkoutRes.ok) {
        console.error("Checkout Failed:", await checkoutRes.text());
        return;
    }

    const checkoutData = await checkoutRes.json();
    const purchaseId = checkoutData.purchaseId;
    console.log("Checkout Success. Purchase ID:", purchaseId);

    // 2. Complete
    console.log(`Step 2: Complete Purchase ${purchaseId}...`);
    const completeRes = await fetch(`${API_URL}/purchases/${purchaseId}/complete`, {
        method: "POST"
    });

    if (!completeRes.ok) {
        console.error("Complete Failed:", await completeRes.text());
        return;
    }

    const completeData = await completeRes.json();
    console.log("Complete Success!");

    if (completeData.voucherId) {
        console.log(`SUCCESS: Assigned Voucher ID: ${completeData.voucherId}`);
        console.log(`Voucher FuelType: ${completeData.voucher?.fuelType}`);
    } else if (completeData.qrCodeId) {
        console.log(`WARNING: Assigned Legacy QR Code ID: ${completeData.qrCodeId} (Expected Voucher)`);
    } else {
        console.error("ERROR: No Voucher or QR Code assigned!", completeData);
    }
}

main().catch(console.error);
