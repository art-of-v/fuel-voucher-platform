INSERT INTO users (id, phone, first_name, last_name, bonus_balance, referral_code, created_at, updated_at) 
VALUES 
  ('usr_1', '+380501234567', 'John', 'Smith', 150.50, 'REF123', NOW(), NOW()), 
  ('usr_2', '+380671234567', 'Emily', 'Johnson', 0.00, 'REF456', NOW(), NOW()), 
  ('usr_3', '+380931234567', 'Michael', 'Davis', 500.00, 'REF789', NOW(), NOW())
ON CONFLICT (id) DO NOTHING;
