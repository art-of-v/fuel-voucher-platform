INSERT INTO fuel_types (id, name, station_id, base_price, discount_price) 
VALUES ('okko-pulls-dp', 'ДП PULLS', 'okko', 58.00, 55.00) 
ON CONFLICT (id) DO UPDATE SET name = 'ДП PULLS';

INSERT INTO fuel_packages (id, station_id, fuel_type_id, fuel_name, liters, price, original_price) VALUES
('okko-pulls-dp-10', 'okko', 'okko-pulls-dp', 'ДП PULLS', 10, 550, 580),
('okko-pulls-dp-20', 'okko', 'okko-pulls-dp', 'ДП PULLS', 20, 1100, 1160),
('okko-pulls-dp-50', 'okko', 'okko-pulls-dp', 'ДП PULLS', 50, 2750, 2900)
ON CONFLICT DO NOTHING;
