-- Удаляем старую таблицу, если она существует
DROP TABLE IF EXISTS CheckColor;

-- Создаем новую таблицу с нужным порядком цветов
CREATE TABLE CheckColor (
    id INTEGER PRIMARY KEY AUTOINCREMENT,  -- Добавим ID для явного порядка
    name TEXT,                            -- Название цвета
    monitor TEXT,                         -- Значение цвета
    sort_order INTEGER                    -- Поле для сортировки
);

-- Вставляем данные в нужном порядке
INSERT INTO CheckColor (name, monitor, sort_order) VALUES
    ('Серый', 'rgb(238, 238, 238)', 1),
    ('Зеленый', 'rgb(113, 214, 113)', 2),
    ('Голубой', 'rgb(127, 127, 228)', 3),
    ('Фиолетовый', 'rgb(194, 94, 176)', 4),
    ('Красный', 'rgb(228, 79, 94)', 5),
    ('Золотой', 'rgb(220, 191, 91)', 6);

-- Создаем индекс для сортировки
CREATE INDEX idx_checkcolor_sort ON CheckColor(sort_order);