import { apiFetch } from '../../../core/api/apiClient';
import type { User } from '../../../core/types/api';

export async function updateUserProfile(
  data: Partial<Pick<User, 'firstName' | 'lastName' | 'email' | 'birthdate'>>,
): Promise<User & { emailChangePending?: boolean }> {
  const body: Record<string, any> = {};
  if (data.firstName) body.firstName = data.firstName;
  if (data.lastName) body.lastName = data.lastName;
  if (data.email) body.email = data.email;
  if (data.birthdate) {
    const trimmed = data.birthdate.trim();
    if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
      body.birthdate = trimmed;
    } else if (trimmed.includes('.')) {
      const parts = trimmed.split('.');
      if (parts.length === 3) {
        const [day, month, year] = parts;
        if (day && month && year && year.length === 4) {
          body.birthdate = `${year}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`;
        } else {
          throw new Error('Invalid birthdate format (expected DD.MM.YYYY)');
        }
      } else {
        throw new Error('Invalid birthdate format (expected DD.MM.YYYY)');
      }
    } else {
      throw new Error('Invalid birthdate format (expected DD.MM.YYYY)');
    }
  }

  const response = await apiFetch('/api/users/update', {
    method: 'POST',
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const errBody = await response.json().catch(() => ({}));
    throw new Error(
      errBody.message || errBody.title || `Error saving (${response.status})`,
    );
  }
  return response.json();
}
