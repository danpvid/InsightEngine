import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_suppliers_data(num_rows=5000):
    data = []

    # Distribuições realistas
    categories = ['Matéria Prima', 'Serviços', 'Equipamentos', 'Software', 'Logística']
    category_weights = [0.3, 0.2, 0.2, 0.15, 0.15]

    statuses = ['Ativo', 'Inativo', 'Suspenso', 'Preferencial']
    status_weights = [0.75, 0.15, 0.05, 0.05]

    payment_terms = ['À vista', '15 dias', '30 dias', '45 dias', '60 dias']
    payment_weights = [0.2, 0.3, 0.3, 0.15, 0.05]

    for i in range(num_rows):
        registration_date = fake.date_between(start_date='-10y', end_date='today')
        last_purchase = registration_date + timedelta(days=random.randint(0, 365*3)) if random.random() > 0.1 else None

        total_purchases = round(np.random.lognormal(10, 1.5), 2) if last_purchase else 0

        row = {
            'ID_Fornecedor': f'FOR{i+1:05d}',
            'Nome_Empresa': fake.company(),
            'CNPJ': fake.cnpj(),
            'Contato': fake.name(),
            'Email': fake.email(),
            'Telefone': fake.phone_number(),
            'Categoria': np.random.choice(categories, p=category_weights),
            'Prazo_Pagamento': np.random.choice(payment_terms, p=payment_weights),
            'Valor_Total_Compras': total_purchases,
            'Ultima_Compra': last_purchase.strftime('%Y-%m-%d') if last_purchase else None,
            'Status': np.random.choice(statuses, p=status_weights),
            'Avaliacao': round(random.uniform(1, 5), 1),
            'Condicoes_Pagamento': random.choice(['Boleto', 'Transferência', 'Cartão', 'Cheque']),
            'Desconto_Medio': round(random.uniform(0, 15), 2),
            'Produtos_Fornecidos': ', '.join([fake.word() for _ in range(random.randint(1, 5))]),
            'Cidade': fake.city(),
            'Estado': fake.state_abbr(),
            'Data_Cadastro': registration_date.strftime('%Y-%m-%d')
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/fornecedores_compras.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'fornecedores_compras.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_suppliers_data()