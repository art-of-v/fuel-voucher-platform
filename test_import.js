import fs from 'fs';
import path from 'path';
import FormData from 'form-data';
import axios from 'axios';

const API_URL = 'http://localhost:4000/api/vouchers/import';
const TEST_DIR = 'C:\\Users\\Artem Vashchuk\\Desktop\\Lemberg test files\\test files';

async function testImport() {
    try {
        const files = fs.readdirSync(TEST_DIR).filter(f => f.endsWith('.pdf'));
        console.log(`Found ${files.length} PDF files.`);

        const formData = new FormData();
        for (const file of files) {
            const filePath = path.join(TEST_DIR, file);
            formData.append('files', fs.createReadStream(filePath));
            console.log(`Adding ${file} to import...`);
        }

        console.log('Sending request to backend...');
        const response = await axios.post(API_URL, formData, {
            headers: formData.getHeaders()
        });

        console.log('Response:', response.data);
        const jobId = response.data.jobId;

        console.log(`Polling status for Job ${jobId}...`);
        let completed = false;
        while (!completed) {
            await new Promise(resolve => setTimeout(resolve, 2000));
            const statusRes = await axios.get(`http://localhost:4000/api/vouchers/import-status/${jobId}`);
            const job = statusRes.data;
            console.log(`Status: ${job.status} | Processed: ${job.processedFiles}/${job.totalFiles} | Success: ${job.successfulFiles} | Failed: ${job.failedFiles}`);

            if (job.status === 'completed' || job.status === 'completed_with_errors' || job.status === 'failed') {
                completed = true;
                console.log('Final Job Data:', JSON.stringify(job, null, 2));
            }
        }
    } catch (error) {
        console.error('Test failed:', error.message);
        if (error.response) {
            console.error('Error Details:', error.response.data);
        }
    }
}

testImport();
