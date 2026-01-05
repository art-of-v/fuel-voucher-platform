jest.mock('./stripeClient', () => ({
    getUncachableStripeClient: jest.fn(),
    getStripePublishableKey: jest.fn().mockResolvedValue('pk_test_mock'),
}));

jest.mock('./twilio', () => ({
    generateVerificationCode: jest.fn().mockReturnValue('123456'),
    sendVerificationCode: jest.fn().mockResolvedValue(true),
}));

global.console.log = jest.fn();
global.console.error = jest.fn();
