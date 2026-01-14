import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.fuelflow.app',
  appName: 'Fuel Flow',
  webDir: 'dist',
  server: {
    // For Android emulator, use 10.0.2.2 to access host machine's localhost
    // For physical device, replace with your computer's IP address
    url: 'http://10.0.2.2:5001',
    cleartext: true
  }
};

export default config;
