const ACCESS_TOKEN_KEY = "admin_access_token";
const REFRESH_TOKEN_KEY = "admin_refresh_token";

export function getStoredAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
}

export function getStoredRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_KEY);
}

export function storeTokens(accessToken: string, refreshToken: string) {
  localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
}

export function clearTokens() {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

export function isLoggedIn(): boolean {
  return !!getStoredAccessToken();
}

export async function sendCode(phoneNumber: string): Promise<void> {
  const res = await fetch(
    `https://fuel-voucher-platform.onrender.com/api/auth/send-code`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ phoneNumber }),
    }
  );
  if (!res.ok) throw new Error(await res.text());
}

export async function verifyCode(
  phoneNumber: string,
  code: string
): Promise<void> {
  const res = await fetch(
    `https://fuel-voucher-platform.onrender.com/api/auth/verify`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ phoneNumber, code }),
    }
  );
  if (!res.ok) throw new Error(await res.text());
  const data = await res.json();
  storeTokens(data.accessToken, data.refreshToken);
}

export interface CurrentUser {
  id: string;
  phone: string;
  userType: string;
  bonusBalance: number;
}

export async function fetchCurrentUser(): Promise<CurrentUser> {
  const token = getStoredAccessToken();
  const res = await fetch(
    `https://fuel-voucher-platform.onrender.com/api/auth/user/me`,
    {
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    }
  );
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = getStoredRefreshToken();
  if (!refreshToken) return false;
  try {
    const res = await fetch(
      `https://fuel-voucher-platform.onrender.com/api/auth/refresh`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken }),
      }
    );
    if (!res.ok) return false;
    const data = await res.json();
    storeTokens(data.accessToken, data.refreshToken);
    return true;
  } catch {
    return false;
  }
}
