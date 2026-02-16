import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_customers_data(num_rows=5000):
    data = []

    # Distribuições realistas
    genders = ['Masculino', 'Feminino', 'Outro']
    gender_weights = [0.48, 0.5, 0.02]

    statuses = ['Ativo', 'Inativo', 'Bloqueado', 'VIP']
    status_weights = [0.7, 0.25, 0.03, 0.02]

    channels = ['Site', 'Indicação', 'Redes Sociais', 'Email Marketing', 'Busca Orgânica']
    channel_weights = [0.3, 0.2, 0.2, 0.15, 0.15]

    for i in range(num_rows):
        registration_date = fake.date_between(start_date='-5y', end_date='today')
        last_purchase = registration_date + timedelta(days=random.randint(0, 365*2)) if random.random() > 0.2 else None

        total_purchases = round(np.random.lognormal(7, 1.5), 2) if last_purchase else 0
        num_orders = random.randint(1, 50) if total_purchases > 0 else 0

        row = {
            'ID_Cliente': f'CLI{i+1:06d}',
            'Nome': fake.name(),
            'CPF_CNPJ': fake.cpf() if random.random() > 0.1 else fake.cnpj(),
            'Email': fake.email(),
            'Telefone': fake.phone_number(),
            'Data_Cadastro': registration_date.strftime('%Y-%m-%d'),
            'Data_Ultima_Compra': last_purchase.strftime('%Y-%m-%d') if last_purchase else None,
            'Valor_Total_Compras': total_purchases,
            'Numero_Pedidos': num_orders,
            'Cidade': fake.city(),
            'Estado': fake.state_abbr(),
            'CEP': fake.postcode(),
            'Idade': random.randint(18, 80),
            'Genero': np.random.choice(genders, p=gender_weights),
            'Renda_Estimada': round(np.random.lognormal(9, 1), 2),
            'Score_Credito': random.randint(0, 1000),
            'Status': np.random.choice(statuses, p=status_weights),
            'Preferencias': ', '.join(random.sample(['Eletrônicos', 'Roupas', 'Casa', 'Esportes', 'Livros'], random.randint(1, 3))),
            'Canal_Aquisicao': np.random.choice(channels, p=channel_weights)
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/dados_clientes.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'dados_clientes.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_customers_data()