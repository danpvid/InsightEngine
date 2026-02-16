import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime

fake = Faker('pt_BR')

def generate_cashflow_data(num_rows=5000):
    data = []

    # Distribuições realistas
    types = ['Entrada', 'Saída']
    type_weights = [0.45, 0.55]

    categories = ['Vendas', 'Salários', 'Fornecedores', 'Impostos', 'Investimentos', 'Despesas Operacionais']
    category_weights = [0.25, 0.2, 0.15, 0.1, 0.1, 0.2]

    accounts = ['Conta Corrente', 'Conta Poupança', 'Caixa', 'Investimentos']
    account_weights = [0.6, 0.2, 0.15, 0.05]

    currencies = ['BRL', 'USD', 'EUR']
    currency_weights = [0.95, 0.04, 0.01]

    # Simular saldo acumulado
    current_balance = 100000.0

    for i in range(num_rows):
        transaction_date = fake.date_between(start_date='-1y', end_date='today')
        transaction_type = np.random.choice(types, p=type_weights)

        if transaction_type == 'Entrada':
            value = round(np.random.lognormal(8, 1.2), 2)
            new_balance = current_balance + value
        else:
            value = round(np.random.lognormal(7, 1.5), 2)
            new_balance = current_balance - value

        row = {
            'Data': transaction_date.strftime('%Y-%m-%d'),
            'Tipo': transaction_type,
            'Descricao': fake.sentence(nb_words=4)[:-1],
            'Valor': value,
            'Categoria': np.random.choice(categories, p=category_weights),
            'Subcategoria': fake.word(),
            'Conta': np.random.choice(accounts, p=account_weights),
            'Saldo_Anterior': round(current_balance, 2),
            'Saldo_Apos': round(new_balance, 2),
            'Responsavel': fake.name(),
            'Comprovante': f'COMP{random.randint(100000, 999999)}',
            'Centro_Custo': fake.word(),
            'Projeto': fake.sentence(nb_words=2)[:-1] if random.random() > 0.7 else None,
            'Moeda': np.random.choice(currencies, p=currency_weights),
            'Taxa_Cambio': round(random.uniform(1, 6), 4) if random.random() > 0.9 else 1.0,
            'Previsao_Realizado': random.choice(['Realizado', 'Previsão']),
            'Observacoes': fake.sentence() if random.random() > 0.8 else None
        }

        current_balance = new_balance
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/financas_fluxo_caixa.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'financas_fluxo_caixa.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_cashflow_data()