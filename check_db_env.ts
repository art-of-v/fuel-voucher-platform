
import 'dotenv/config';
import { storage } from './server/storage';
async function main() {
  const v = await storage.getVouchers({ limit: 100 });
  console.log('Total Vouchers:', v.total);
  console.log('Sample:', v.data.slice(0, 1));
  process.exit(0);
}
main();

