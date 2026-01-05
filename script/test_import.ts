
import fs from 'fs';
import path from 'path';
import FormData from 'form-data';
import fetch from 'node-fetch';

async function testImport() {
    try {
        const filePath = path.join(process.cwd(), 'test.pdf');
        if (!fs.existsSync(filePath)) {
            console.error("test.pdf not found in root!");
            return;
        }

        console.log("Found test.pdf, size:", fs.statSync(filePath).size);

        const form = new FormData();
        form.append('files', fs.createReadStream(filePath));
        form.append('provider', 'OKKO');
        form.append('fuelType', 'Diesel Euro');

        console.log("Uploading to http://localhost:5000/api/vouchers/import...");

        const res = await fetch('http://localhost:5000/api/vouchers/import', {
            method: 'POST',
            body: form
        });

        if (!res.ok) {
            console.error("Upload failed:", res.status, await res.text());
            return;
        }

        const json: any = await res.json();
        console.log("Upload success! Job create:", json);

        const jobId = json.jobId;
        if (!jobId) {
            console.error("No Job ID returned");
            return;
        }

        // Poll for status
        console.log(`Polling status for Job ${jobId}...`);

        let attempts = 0;
        while (attempts < 30) {
            const statusRes = await fetch(`http://localhost:5000/api/vouchers/import-status/${jobId}`);
            const statusJson: any = await statusRes.json();

            console.log(`[Attempt ${attempts}] Status: ${statusJson.status}, Processed: ${statusJson.processedFiles}/${statusJson.totalFiles}`);

            if (statusJson.status === 'completed' || statusJson.status === 'completed_with_errors' || statusJson.status === 'failed') {
                console.log("Job Finished!");
                console.log("Final Report:", JSON.stringify(statusJson, null, 2));
                break;
            }

            await new Promise(r => setTimeout(r, 1000));
            attempts++;
        }

    } catch (error) {
        console.error("Test failed:", error);
    }
}

testImport();
