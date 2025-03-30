-- Обновляем существующие записи, убирая "color: " и ";"
UPDATE Deals
SET RowColor = 
    REPLACE(
        REPLACE(
            TRIM(RowColor), 
            'color: ', 
            ''
        ),
        ';',
        ''
    )
WHERE RowColor LIKE 'color:%';