import { apiFetch } from '../../../core/api/apiClient';
import type { User } from '../../../core/types/api';

export async function updateUserProfile(
  data: Partial<Pick<User, 'firstName' | 'lastName' | 'email' | 'birthdate'>>,
): Promise<User> {
  const body: Record<string, any> = {};
  if (data.firstName) body.firstName = data.firstName;
  if (data.lastName) body.lastName = data.lastName;
  if (data.email) body.email = data.email;
  if (data.birthdate) {
    const [day, month, year] = data.birthdate.split('.');
    body.birthdate = `${year}-${month}-${day}`;
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
