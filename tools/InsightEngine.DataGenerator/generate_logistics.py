import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_logistics_data(num_rows=5000):
    data = []

    # Distribuições realistas
    carriers = ['Correios', 'FedEx', 'DHL', 'Transportadora XYZ', 'Jadlog']
    carrier_weights = [0.4, 0.2, 0.15, 0.15, 0.1]

    statuses = ['Entregue', 'Em Trânsito', 'Atrasado', 'Extraviado', 'Devolvido']
    status_weights = [0.7, 0.2, 0.05, 0.03, 0.02]

    for i in range(num_rows):
        departure_date = fake.date_between(start_date='-6m', end_date='today')
        expected_date = departure_date + timedelta(days=random.randint(1, 10))
        actual_date = expected_date + timedelta(days=random.randint(-2, 5)) if random.random() > 0.1 else None

        row = {
            'ID_Entrega': f'ENT{i+1:06d}',
            'Pedido_ID': f'PED{random.randint(1, 10000):06d}',
            'Transportadora': np.random.choice(carriers, p=carrier_weights),
            'Data_Saida': departure_date.strftime('%Y-%m-%d'),
            'Data_Prevista': expected_date.strftime('%Y-%m-%d'),
            'Data_Entrega': actual_date.strftime('%Y-%m-%d') if actual_date else None,
            'Status': np.random.choice(statuses, p=status_weights),
            'Peso_Kg': round(np.random.normal(5, 3), 2),
            'Volume_M3': round(np.random.normal(0.05, 0.03), 3),
            'Valor_Frete': round(np.random.normal(25, 15), 2),
            'Destinatario': fake.name(),
            'Endereco': fake.street_address(),
            'Cidade': fake.city(),
            'Estado': fake.state_abbr(),
            'CEP': fake.postcode(),
            'Rastreamento': f'BR{random.randint(100000000, 999999999)}',
            'Motivo_Atraso': fake.sentence() if random.random() > 0.85 else None,
            'Tentativas_Entrega': random.randint(1, 3),
            'Responsavel': fake.name()
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/logistica_entregas.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'logistica_entregas.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_logistics_data()