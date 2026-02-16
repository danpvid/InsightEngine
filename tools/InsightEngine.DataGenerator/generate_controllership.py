import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime

fake = Faker('pt_BR')

def generate_controllership_data(num_rows=5000):
    data = []

    # Distribuições realistas
    tipos = ['Receita', 'Despesa', 'Transferência', 'Ajuste']
    tipo_weights = [0.4, 0.45, 0.1, 0.05]

    centros_custo = ['Vendas', 'Administrativo', 'Produção', 'Logística', 'Marketing']
    centro_weights = [0.3, 0.2, 0.25, 0.15, 0.1]

    moedas = ['BRL', 'USD', 'EUR']
    moeda_weights = [0.9, 0.08, 0.02]

    for i in range(num_rows):
        data_lancamento = fake.date_between(start_date='-1y', end_date='today')
        valor_base = np.random.lognormal(8, 1.5)  # Distribuição log-normal para valores

        row = {
            'Data_Lancamento': data_lancamento.strftime('%Y-%m-%d'),
            'Conta_Debito': f'1.{random.randint(1000, 9999)}',
            'Conta_Credito': f'2.{random.randint(1000, 9999)}',
            'Valor': round(valor_base, 2),
            'Historico': fake.sentence(nb_words=5)[:-1],
            'Tipo_Lancamento': np.random.choice(tipos, p=tipo_weights),
            'Centro_Custo': np.random.choice(centros_custo, p=centro_weights),
            'Filial': f'FIL{random.randint(1, 50):02d}',
            'Documento': f'DOC{random.randint(100000, 999999)}',
            'Fornecedor_Cliente': fake.company() if random.random() > 0.5 else fake.name(),
            'Moeda': np.random.choice(moedas, p=moeda_weights),
            'Taxa_Cambio': round(random.uniform(1, 6), 4) if random.random() > 0.9 else 1.0,
            'Competencia': f'{data_lancamento.year}-{data_lancamento.month:02d}',
            'Usuario': fake.name(),
            'Aprovado': random.choice([True, False]),
            'Observacoes': fake.sentence() if random.random() > 0.7 else None,
            'Categoria': fake.word(),
            'Subcategoria': fake.word()
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/controladoria_contabilidade.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'controladoria_contabilidade.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_controllership_data()