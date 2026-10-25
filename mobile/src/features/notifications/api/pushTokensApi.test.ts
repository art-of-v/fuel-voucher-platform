import { apiRequest } from '../../../core/api/apiClient';
import { parkPushToken } from './pushTokensApi';

jest.mock('../../../core/api/apiClient', () => ({
  apiRequest: jest.fn(),
}));

const apiRequestMock = apiRequest as jest.Mock;

describe('parkPushToken', () => {
  beforeEach(() => {
    apiRequestMock.mockReset();
  });

  it('parks the token on the park route', async () => {
    apiRequestMock.mockResolvedValue({ ok: true });

    await parkPushToken();

    // Parking is not deregistering: the device keeps its address for the sign-in code.
    expect(apiRequestMock).toHaveBeenCalledWith('POST', '/api/notifications/push-tokens/park');
  });

  it('never blocks a logout on a failed park', async () => {
    apiRequestMock.mockRejectedValue(new Error('offline'));

    await expect(parkPushToken()).resolves.toBeUndefined();
  });
});
