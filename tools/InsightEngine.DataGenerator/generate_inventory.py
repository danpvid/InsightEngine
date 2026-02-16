import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_inventory_data(num_rows=5000):
    data = []

    # Distribuições realistas
    categories = ['Eletrônicos', 'Roupas', 'Alimentos', 'Ferramentas', 'Móveis']
    category_weights = [0.25, 0.2, 0.2, 0.15, 0.2]

    movement_types = ['Entrada', 'Saída', 'Ajuste', 'Transferência']
    movement_weights = [0.3, 0.5, 0.15, 0.05]

    statuses = ['Disponível', 'Reservado', 'Danificado', 'Vencido']
    status_weights = [0.8, 0.1, 0.05, 0.05]

    for i in range(num_rows):
        movement_date = fake.date_between(start_date='-1y', end_date='today')
        unit_value = round(np.random.lognormal(5, 1.5), 2)
        qty_moved = random.randint(1, 500)
        current_stock = random.randint(0, 10000)

        row = {
            'SKU': f'SKU{random.randint(10000, 99999)}',
            'Nome_Produto': fake.sentence(nb_words=3)[:-1],
            'Categoria': np.random.choice(categories, p=category_weights),
            'Fornecedor': fake.company(),
            'Quantidade_Estoque': current_stock,
            'Valor_Unitario': unit_value,
            'Valor_Total': round(current_stock * unit_value, 2),
            'Localizacao': f'Armazém {random.randint(1, 10)} - Prateleira {random.randint(1, 100)}',
            'Data_Ultima_Movimentacao': movement_date.strftime('%Y-%m-%d'),
            'Tipo_Movimentacao': np.random.choice(movement_types, p=movement_weights),
            'Quantidade_Movimentada': qty_moved,
            'Saldo_Apos_Movimentacao': current_stock,
            'Motivo': fake.sentence(nb_words=4)[:-1],
            'Responsavel': fake.name(),
            'Data_Vencimento': (movement_date + timedelta(days=random.randint(30, 365*2))).strftime('%Y-%m-%d') if random.random() > 0.7 else None,
            'Lote': f'LOTE{random.randint(1000, 9999)}',
            'Status': np.random.choice(statuses, p=status_weights)
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/inventario_produtos.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'inventario_produtos.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_inventory_data()