export const API_BASE_URL = import.meta.env.VITE_API_URL || (import.meta.env.PROD ? 'https://fuel-voucher-platform.onrender.com' : 'http://localhost:5000');
export const NODE_API_URL = import.meta.env.VITE_NODE_API_URL || (import.meta.env.PROD ? 'https://fuel-voucher-platform.onrender.com' : 'http://localhost:3000');
export default {
  API_BASE_URL,
  NODE_API_URL
};
