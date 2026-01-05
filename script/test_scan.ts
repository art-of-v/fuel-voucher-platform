
import { fromPath } from "pdf2pic";
import path from "path";
import fs from "fs";
import { analyzeVoucherImage } from "../server/services/voucher_analysis";

// Helper to convert PDF page to Buffer
async function convertPdfToBuffer(filePath: string): Promise<Buffer> {
    if (filePath.toLowerCase().endsWith('.png') || filePath.toLowerCase().endsWith('.jpg')) {
        return fs.readFileSync(filePath);
    }

    const savePath = path.resolve("./temp_test");
    if (!fs.existsSync(savePath)) fs.mkdirSync(savePath);

    const converter = fromPath(filePath, {
        density: 300,
        saveFilename: "test_page",
        savePath: savePath,
        format: "png",
        width: 2000,
        height: 2000
    });

    const result = await converter(1);
    const imgPath = result.path;
    return fs.readFileSync(imgPath);
}

async function run() {
    // Pick a PDF found in uploads
    // const pdfPath = path.resolve("client/public/uploads/vouchers/0b851610-4d0d-4edf-a6e8-da40c849301d.pdf");

    // Actually, let's search for the NEWEST pdf in uploads to likely match user's upload?
    const uploadsDir = path.resolve("client/public/uploads/vouchers");
    const files = fs.readdirSync(uploadsDir).map(f => ({ name: f, time: fs.statSync(path.join(uploadsDir, f)).mtime.getTime() })).sort((a, b) => b.time - a.time);

    if (files.length === 0) { console.error("No PDFs found"); return; }

    const pdfPath = path.join(uploadsDir, files[0].name);
    console.log(`Testing with file: ${pdfPath}`);

    try {
        const imageBuffer = await convertPdfToBuffer(pdfPath);
        console.log("Converted to PNG. Analyzing...");

        const results = await analyzeVoucherImage(imageBuffer);

        console.log("\n--- ANALYSIS RESULTS ---");
        results.forEach((r, i) => {
            console.log(`\nVoucher #${i + 1}:`);
            console.log(`  Fuel: ${r.metadata.fuelType}`);
            console.log(`  Amount: ${r.metadata.amount}`);
            console.log(`  Date: ${r.metadata.expirationDate}`);
            console.log(`  ID: ${r.metadata.externalId}`);
        });

    } catch (e) {
        console.error("Test Error:", e);
    }
}

run();
