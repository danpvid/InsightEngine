import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_marketing_data(num_rows=5000):
    data = []

    # Distribuições realistas
    channels = ['Google Ads', 'Facebook', 'Instagram', 'LinkedIn', 'Email Marketing']
    channel_weights = [0.3, 0.25, 0.2, 0.15, 0.1]

    statuses = ['Ativa', 'Pausada', 'Finalizada', 'Cancelada']
    status_weights = [0.4, 0.2, 0.35, 0.05]

    segments = ['18-24', '25-34', '35-44', '45-54', '55+']
    segment_weights = [0.2, 0.3, 0.25, 0.15, 0.1]

    for i in range(num_rows):
        start_date = fake.date_between(start_date='-1y', end_date='today')
        end_date = start_date + timedelta(days=random.randint(7, 90))

        investment = round(np.random.lognormal(7, 1.2), 2)
        impressions = int(np.random.lognormal(12, 1.5))
        clicks = int(impressions * np.random.beta(2, 20))  # CTR baixo
        conversions = int(clicks * np.random.beta(1, 10))  # Conversão baixa

        ctr = (clicks / impressions) * 100 if impressions > 0 else 0
        cpc = investment / clicks if clicks > 0 else 0
        cpa = investment / conversions if conversions > 0 else 0
        roi = (conversions * 50 - investment) / investment * 100  # Assumindo valor médio de conversão

        row = {
            'ID_Campanha': f'CAMP{i+1:05d}',
            'Nome_Campanha': fake.sentence(nb_words=4)[:-1],
            'Data_Inicio': start_date.strftime('%Y-%m-%d'),
            'Data_Fim': end_date.strftime('%Y-%m-%d'),
            'Canal': np.random.choice(channels, p=channel_weights),
            'Investimento': investment,
            'Impressoes': impressions,
            'Cliques': clicks,
            'Conversoes': conversions,
            'CTR': round(ctr, 2),
            'CPC': round(cpc, 2),
            'CPA': round(cpa, 2),
            'ROI': round(roi, 2),
            'Publico_Alvo': f'{random.randint(10000, 100000)} pessoas',
            'Segmento': np.random.choice(segments, p=segment_weights),
            'Status': np.random.choice(statuses, p=status_weights),
            'Responsavel': fake.name(),
            'Objetivo': random.choice(['Aumento Vendas', 'Geração Leads', 'Brand Awareness', 'Retenção']),
            'Metricas_Adicionais': f'Engajamento: {random.randint(1, 20)}%, Bounce Rate: {random.randint(30, 80)}%'
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/marketing_digital.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'marketing_digital.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_marketing_data()